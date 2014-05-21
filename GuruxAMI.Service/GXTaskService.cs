//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using GuruxAMI.Common.Messages;
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Data;
using ServiceStack.OrmLite;
using System;
using System.Xml.Linq;
using GuruxAMI.Server;
using System.Text;
using System.Linq;

#if !SS4
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
#else
using ServiceStack;
using ServiceStack.Auth;
#endif

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles Task functionality.
    /// </summary>
    [Authenticate]
    internal class GXTaskService : GXService
    {
        /// <summary>
        /// Can user, add or remove users.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static public bool IsLimitedAccess(IAuthSession s)
        {
            int value = Convert.ToInt32(s.Roles[0]);
            return (value & (int)UserAccessRights.LimitedAccess) == (int)UserAccessRights.LimitedAccess;
        }

        /// <summary>
        /// Add new task. Tasks can't update.
        /// </summary>
        public GXTaskUpdateResponse Put(GXTaskUpdateRequest request)
        {
            List<GXAmiTask> executedTasks = new List<GXAmiTask>();
            List<GXEventsItem> events = new List<GXEventsItem>();
            long adderId = 0;
            lock (Db)
            {                
                IAuthSession s = this.GetSession(false);                
                bool superAdmin = false;
                Guid dcGuid = Guid.Empty;
                //If failed task adder id DC.
                if (long.TryParse(s.Id, out adderId))
                {
                    //Is limited access.
                    if (IsLimitedAccess(s))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                }
                else //Adder is DC.
                {
                    if (!GXBasicAuthProvider.IsGuid(s.UserAuthName, out dcGuid))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(q => q.Guid == dcGuid);
                    if (list.Count != 1)
                    {
                        throw new ArgumentException("Access denied.");
                    }
                }
                //Add tasks.
                foreach (GXAmiTask it in request.Tasks)
                {
                    //If task is already added.
                    if (it.Id != 0)
                    {
                        throw new ArgumentException("Invalid target.");
                    }
                    if (it.TargetID == 0)
                    {
                        if ((it.DataCollectorGuid == Guid.Empty && dcGuid == Guid.Empty) ||
                            (it.TaskType != TaskType.MediaOpen &&
                            it.TaskType != TaskType.MediaGetProperty &&
                            it.TaskType != TaskType.MediaSetProperty &&
                            it.TaskType != TaskType.MediaClose &&
                            it.TaskType != TaskType.MediaWrite &&
                            it.TaskType != TaskType.MediaError &&
                            it.TaskType != TaskType.MediaState))
                        {
                            throw new ArgumentException("Invalid target.");
                        }
                        //Get DC ID
                        List<GXAmiDataCollector> tmp;
                        if (it.DataCollectorGuid != Guid.Empty)//If target is selected.
                        {
                            tmp = Db.Select<GXAmiDataCollector>(q => q.Guid == it.DataCollectorGuid);
                        }
                        else//If DC is sending data.
                        {
                            tmp = Db.Select<GXAmiDataCollector>(q => q.Guid == dcGuid);
                        }
                        if (tmp.Count != 1)
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        /*
                        if (it.DataCollectorGuid != Guid.Empty)
                        {
                            it.TargetID = tmp[0].Id;
                        }
                         * */
                        it.TargetID = tmp[0].Id;
                    }
                    else //Check that task is not added yet.
                    {
                        string query = string.Format("SELECT COUNT(*) FROM {0} WHERE TargetID = {1} AND TaskType = {2}",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db), 
                            it.TargetID,
                            it.TaskTypeAsInt);
                        if (Db.SqlScalar<int>(query, null) != 0)
                        {
                            continue;
                        }
                    }
                    //TODO: Check that user has access to the target.                
                    it.State = TaskState.Pending;
                    if (adderId != 0)
                    {
                        it.UserID = adderId;
                    }
                    else
                    {
                        it.UserID = null;
                    }
                    it.CreationTime = DateTime.Now;
                    it.SenderDataCollectorGuid = dcGuid;
                    UpdateTaskTarget(it);
                    events.Add(new GXEventsItem(ActionTargets.Task, Actions.Add, it));
                    executedTasks.Add(it);
                }                
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    foreach (GXAmiTask it in executedTasks)
                    {
                        //Remove earlier media state change tasks.
                        if (it.TaskType == TaskType.MediaState)
                        {
                            List<GXAmiTask> tmp = Db.Select<GXAmiTask>(q => q.DataCollectorGuid == it.DataCollectorGuid && q.TaskTypeAsInt == (int) TaskType.MediaState);
                            foreach (GXAmiTask task in tmp)
                            {
                                task.State = TaskState.Succeeded;
                                events.Add(new GXEventsItem(ActionTargets.Task, Actions.Remove, task));
                            }
                            Db.Delete<GXAmiTask>(tmp.ToArray());
                        }
                        Db.Insert(it);
#if !SS4
                        it.Id = (ulong)Db.GetLastInsertId();
#else
                        it.Id = (ulong)Db.LastInsertId();
#endif
                        if (it.Data != null)
                        {
                            foreach (string value in SplitByLength(it.Data, 255))
                            {
                                GXAmiTaskData d = new GXAmiTaskData();
                                d.TaskId = it.Id;
                                d.Data = value;
                                Db.Insert(d);
                            }
                        }
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXTaskUpdateResponse(executedTasks.ToArray());
        }

        internal static string[] SplitByLength(string str, int maxLength)
        {
            int cnt = str.Length / maxLength;
            if (str.Length % maxLength != 0)
            {
                ++cnt;
            }
            List<string> items = new List<string>(cnt);
            for (int index = 0; index < str.Length; index += maxLength)
            {
                items.Add(str.Substring(index, Math.Min(maxLength, str.Length - index)));
            }
            return items.ToArray();
        }

        /// <summary>
        /// Update sender and target to string so it's easier to show them on UI.
        /// </summary>
        /// <param name="task"></param>
        void UpdateTaskTarget(GXAmiTask task)
        {           
            if (string.IsNullOrEmpty(task.SenderAsString))
            {
                //Update sender name.
                if (task.TargetType == TargetType.Device)
                {
                    task.SenderAsString = Db.GetById<GXAmiDevice>(task.TargetID).Name;
                }
                else if (task.UserID != null)
                {
                    task.SenderAsString = Db.GetById<GXAmiUser>(task.UserID.Value).Name;
                }
                else if (task.TargetID != 0 && (task.TargetType == TargetType.DataCollector || task.TargetType == TargetType.Media))
                {
                    task.SenderAsString = Db.Select<GXAmiDataCollector>(q => q.Guid == task.DataCollectorGuid)[0].Name;
                }
            }
            //Update target name.
            if (string.IsNullOrEmpty(task.TargetAsString))
            {
                //Update target name.
                if (task.TargetType == TargetType.Device)
                {
                    task.TargetAsString = Db.GetById<GXAmiDevice>(task.TargetID).Name;
                }               
                else if (task.TargetID != 0 && (task.TargetType == TargetType.DataCollector || task.TargetType == TargetType.Media))
                {
                    task.TargetAsString = Db.GetById<GXAmiDataCollector>(task.TargetID).Name;
                }
                else if (task.UserID != null)
                {
                    task.TargetAsString = Db.GetById<GXAmiUser>(task.UserID.Value).Name;
                }
            }
            
        }

        /// <summary>
        /// Mark task(s) as claimed.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTasksClaimResponse Post(GXTasksClaimRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            List<GXClaimedTask> infoList = new List<GXClaimedTask>();
            AppHost host = this.ResolveService<AppHost>();
            Guid guid;
            IAuthSession s = this.GetSession(false);
            if (!GXBasicAuthProvider.IsGuid(s.UserAuthName, out guid))
            {
                throw new ArgumentException("Access denied.");
            }
            lock (Db)
            {                                
                DateTime claimTime = DateTime.Now;
                foreach (ulong it in request.TaskIDs)
                {
                    GXAmiDataCollector collector = null;
                    //Try to get wanted task. If that is taken get next available task.
                    List<GXAmiTask> tasks = Db.Select<GXAmiTask>(q => q.Id == it && q.ClaimTime == null);
                    //Make sure that no one else is has not claim the task.
                    GXAmiTask task = null;
                    bool wantedTask = tasks.Count != 0;
                    if (wantedTask)
                    {
#if !SS4
                        task = Db.GetById<GXAmiTask>(it);
#else
                        task = Db.SingleById<GXAmiTask>(it);
#endif
                    }
                    else
                    {
                        tasks = Db.Select<GXAmiTask>(q => q.ClaimTime == null);
                        if (tasks.Count != 0)
                        {
                            task = tasks[0];
                        }
                    }                                            
                    if (task != null)
                    {
                        task.Data = GetData(it, false);
                        task.SenderDataCollectorGuid = guid;
                        //If DC is clamiming the task for it self.
                        if (task.TargetDeviceID == null && task.DataCollectorGuid != Guid.Empty)
                        {
                            //DC can claim only own tasks.
                            if (task.DataCollectorGuid != guid)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            List<GXAmiDataCollector> collectors = Db.Select<GXAmiDataCollector>(q => q.Guid == guid);
                            if (collectors.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            collector = collectors[0];
                        }
                        else //If DC is claiming device task.
                        {                            
                            //Get list of DC that can access Device.
                            string query = string.Format("SELECT DISTINCT {0}.* FROM {0} INNER JOIN {1} ON ({0}.ID = {1}.DataCollectorId OR {1}.DataCollectorId IS NULL) WHERE DeviceID = {2} AND Guid = '{3}'",
                                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db), 
                                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                                        task.TargetDeviceID.Value,
                                        guid.ToString().Replace("-", ""));
                            List<GXAmiDataCollector> collectors = Db.Select<GXAmiDataCollector>(query);
                            //If DC can't access task.
                            if (collectors.Count == 0)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            collector = collectors[0];
                        }
                        //Find device template Guid.
                        if (task.TargetDeviceID != null)
                        {
                            string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.ProfileId WHERE {1}.ID = {2}",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                task.TargetDeviceID.Value);
                            List<GXAmiDeviceProfile> profiles = Db.Select<GXAmiDeviceProfile>(query);
                            if (profiles.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            GXClaimedTask info = new GXClaimedTask();
                            if (!wantedTask)
                            {
                                info.Task = task;
                            }
                            info.DeviceProfile = profiles[0].Guid;
                            query = string.Format("SELECT {0}.* FROM {0} WHERE {0}.ID = {1}",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                task.TargetDeviceID.Value);
                            List<GXAmiDevice> devices = Db.Select<GXAmiDevice>(query);
                            if (devices.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            info.Device = devices[0];
                            info.Device.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == info.Device.Id).ToArray();
                            info.Device.Categories = Db.Select<GXAmiCategory>(q => q.DeviceID == info.Device.Id).ToArray();
                            foreach (GXAmiCategory cat in info.Device.Categories)
                            {
                                cat.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == cat.Id).ToArray();
                                cat.Properties = Db.Select<GXAmiProperty>(q => q.ParentID == cat.Id).ToArray();
                                foreach (GXAmiProperty p in cat.Properties)
                                {
                                    p.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == p.Id).ToArray();
                                }
                            }
                            info.Device.Tables = Db.Select<GXAmiDataTable>(q => q.DeviceID == info.Device.Id).ToArray();
                            foreach (GXAmiDataTable table in info.Device.Tables)
                            {
                                table.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == table.Id).ToArray();
                                table.Columns = Db.Select<GXAmiProperty>(q => q.ParentID == table.Id).ToArray();
                                foreach (GXAmiProperty p in table.Columns)
                                {
                                    p.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == p.Id).ToArray();
                                }
                            }
                            info.Device.ProfileGuid = profiles[0].Guid;
                            List<GXAmiDeviceMedia> Medias = Db.Select<GXAmiDeviceMedia>(q => q.DeviceId == info.Device.Id);
                            foreach(GXAmiDeviceMedia it2 in Medias)
                            {
                                if (it2.DataCollectorId == null || it2.DataCollectorId == collector.Id)
                                {
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(it2.Name, new KeyValuePair<string, string>(it2.Name, it2.Settings)));
                                }
                            }
                            info.Data = task.Data;
                            infoList.Add(info);
                        }
                        else //DC claims the task to itself.
                        {
                            GXClaimedTask info = new GXClaimedTask();
                            if (task.TargetType == TargetType.Media)
                            {
                                if (task.TaskType == TaskType.MediaOpen)
                                {
                                    string[] data = task.Data.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                                    info.MediaSettings.Clear();
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(data[0], new KeyValuePair<string, string>(data[1], data[2])));                                    
                                    task.Data = null;
                                }
                                else if (task.TaskType == TaskType.MediaClose)
                                {
                                    string[] data = task.Data.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                                    info.MediaSettings.Clear();
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(data[0], new KeyValuePair<string, string>(data[1], "")));                                    
                                    task.Data = null;
                                }
                                else if (task.TaskType == TaskType.MediaWrite)
                                {
                                    string[] data = task.Data.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                                    info.MediaSettings.Clear();
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(data[0], new KeyValuePair<string, string>(data[1], "")));
                                    task.Data = data[2];
                                }
                                else if (task.TaskType == TaskType.MediaGetProperty)
                                {
                                    string[] data = task.Data.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                                    info.MediaSettings.Clear();
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(data[0], new KeyValuePair<string, string>(data[1], "")));
                                    task.Data = data[2];
                                }
                                else if (task.TaskType == TaskType.MediaSetProperty)
                                {
                                    string[] data = task.Data.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                                    info.MediaSettings.Clear();
                                    info.MediaSettings.Add(new KeyValuePair<string, KeyValuePair<string, string>>(data[0], new KeyValuePair<string, string>(data[1], "")));
                                    task.Data = data[2] + Environment.NewLine + data[3];
                                }
                            }
                            info.DataCollectorID = collector.Id;
                            info.Data = task.Data;
                            infoList.Add(info);
                        }
                        task.ClaimTime = claimTime;
                        task.State = TaskState.Processing;
                        task.DataCollectorID = collector.Id;
                        task.DataCollectorGuid = guid;
                        task.SenderDataCollectorGuid = guid;
                        Db.Update(task);
                        events.Add(new GXEventsItem(ActionTargets.Task, Actions.Edit, task));                        
                    }
                }
            }                        
            //Notify that task is claimed.                        
            host.SetEvents(Db, this.Request, 0, events);
            return new GXTasksClaimResponse(infoList.ToArray());
        }

        /// <summary>
        /// Get list of available tasks.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTasksResponse Post(GXTasksRequest request)
        {
            lock (Db)
            {                  
                IAuthSession s = this.GetSession(false);
                int id = 0;
                List<GXAmiTask> list = new List<GXAmiTask>();
                if (int.TryParse(s.Id, out id))
                {
                    if (id == 0)
                    {
                        throw new ArgumentException("Remove failed. Invalid session ID.");
                    }
                    bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                    //Get tasks by ID.
                    if (request.TaskIDs != null && request.TaskIDs.Length != 0)
                    {
                        foreach (ulong it in request.TaskIDs)
                        {
                            if (it == 0)
                            {
                                throw new ArgumentException("Invalid Task ID.");
                            }
                            //Only task creator or super admin can access tasks.                    
                            if (!superAdmin)
                            {
                                GXAmiTask task;
                                if (request.Log)
                                {
#if !SS4
                                    task = Db.QueryById<GXAmiTaskLog>(it);
#else
                                    task = Db.SingleById<GXAmiTaskLog>(it);
#endif                                   
                                }
                                else
                                {
#if !SS4
                                    task = Db.QueryById<GXAmiTask>(it);
#else
                                    task = Db.SingleById<GXAmiTask>(it);
#endif                                   
                                }
                                //If task is removed.
                                if (task != null)
                                {
                                    if (task.UserID != id)
                                    {
                                        throw new ArgumentException("Access denied.");
                                    }
                                    list.Add(task);
                                }
                            }
                            if (request.Log)
                            {
                                list.AddRange(Db.Select<GXAmiTaskLog>(q => q.Id == it).ToArray());
                            }
                            else
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.Id == it));
                            }
                        }
                    }
                    //Get tasks by user ID.
                    else if (request.UserIDs != null && request.UserIDs.Length != 0)
                    {
                        foreach (long uid in request.UserIDs)
                        {
                            if (uid == 0)
                            {
                                throw new ArgumentException("Invalid User ID.");
                            }
                            //Only task creator or super admin can remove tasks.                    
                            if (!superAdmin)
                            {
                                if (uid != id)
                                {
                                    throw new ArgumentException("Access denied. Only task creator or super admin can access task.");
                                }
                            }
#if !SS4
                            SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                            SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif                                                                
                            if (request.State != TaskState.All)
                            {
                                ev.Where(q => q.UserID == uid);
                                if (request.Descending)
                                {
                                    ev.OrderByDescending(q => q.Id);
                                }
                                list.AddRange(Db.Select<GXAmiTask>(ev));
                            }
                            else
                            {
                                ev.Where(q => q.UserID == uid && q.State == request.State);
                                if (request.Descending)
                                {
                                    ev.OrderByDescending(q => q.Id);
                                }
                                list.AddRange(Db.Select<GXAmiTask>(ev));
                            }
                        }
                    }
                    //Get tasks by user group ID.
                    else if (request.UserGroupIDs != null && request.UserGroupIDs.Length != 0)
                    {
                        foreach (long ugid in request.UserGroupIDs)
                        {
                            if (ugid == 0)
                            {
                                throw new ArgumentException("Invalid User Group ID.");
                            }
                            List<GXAmiUser> users = GXUserService.GetUsers(s, Db, id, ugid, false, true, null, SearchOperator.None, SearchType.All);
                            foreach (GXAmiUser user in users)
                            {
                                //Only task creator or super admin can remove tasks.                    
                                if (!superAdmin)
                                {
                                    if (user.Id != id)
                                    {
                                        throw new ArgumentException("Access denied. Only task creator or super admin can access task.");
                                    }
                                }
                                if (request.State != TaskState.All)
                                {
                                    if (request.Log)
                                    {
#if !SS4
                                        SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                        SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                        ev.Where(q => q.UserID == user.Id && q.State == request.State);
                                        if (request.Descending)
                                        {
                                            ev.OrderByDescending(q => q.Id);
                                        }
                                        list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                    }
                                    else
                                    {
#if !SS4
                                        SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                        SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif                                                                
                                        ev.Where(q => q.UserID == user.Id && q.State == request.State);
                                        if (request.Descending)
                                        {
                                            ev.OrderByDescending(q => q.Id);
                                        }
                                        list.AddRange(Db.Select<GXAmiTask>(ev));
                                    }
                                    
                                }
                                else
                                {
                                    if (request.Log)
                                    {
#if !SS4
                                        SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                        SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                        ev.Where(q => q.UserID == user.Id);
                                        if (request.Descending)
                                        {
                                            ev.OrderByDescending(q => q.Id);
                                        }
                                        list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                    }
                                    else
                                    {
#if !SS4
                                        SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                        SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif                                                                                                        
                                        ev.Where(q => q.UserID == user.Id);
                                        if (request.Descending)
                                        {
                                            ev.OrderByDescending(q => q.Id);
                                        }
                                        list.AddRange(Db.Select<GXAmiTask>(ev));
                                    }                                    
                                }
                            }
                        }
                    }
                    //Get tasks by device ID.
                    else if (request.DeviceIDs != null && request.DeviceIDs.Length != 0)
                    {                        
                        foreach (ulong dId in request.DeviceIDs)
                        {
                            if (dId == 0)
                            {
                                throw new ArgumentException("Invalid Device ID.");
                            }
                            if (!superAdmin && GXDeviceService.GetDevices(s, Db, id, 0, 0, dId, false).Count == 0)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            if (request.State != TaskState.All)
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                                                    
                                    ev.Where(q => q.TargetDeviceID == dId && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif                                                                                                    
                                    ev.Where(q => q.TargetDeviceID == dId && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTask>(ev));
                                }                                
                            }
                            else
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.TargetDeviceID == dId);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    ev.Where(q => q.TargetDeviceID == dId);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTask>(ev));
                                }                                
                            }
                        }
                    }
                    //Get tasks by device group ID.
                    else if (request.DeviceGroupIDs != null && request.DeviceGroupIDs.Length != 0)
                    {
                        foreach (ulong dgId in request.DeviceGroupIDs)
                        {
                            if (dgId == 0)
                            {
                                throw new ArgumentException("Invalid Device Group ID.");
                            }
                            if (!superAdmin && !GXDeviceGroupService.CanUserAccessDeviceGroup(Db, id, dgId))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            if (request.State != TaskState.All)
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.TargetID == dgId && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    ev.Where(q => q.TargetID == dgId && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTask>(ev));
                                }                                
                            }
                            else
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.TargetID == dgId);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif                                                                
                                    ev.Where(q => q.TargetID == dgId);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTask>(ev));
                                }
                                
                            }
                        }
                    }
                    else //Select all tasks.
                    {
                        if (superAdmin)
                        {
                            if (request.State != TaskState.All)
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    ev.Where(q => q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list = Db.Select<GXAmiTask>(ev);
                                }
                                
                            }
                            else
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list = Db.Select<GXAmiTask>(ev);
                                }                                
                            }
                        }
                        else
                        {
                            if (request.State != TaskState.All)
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.UserID == id && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    ev.Where(q => q.UserID == id && q.State == request.State);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list = Db.Select<GXAmiTask>(ev);
                                }
                                
                            }
                            else
                            {
                                if (request.Log)
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTaskLog>();
#else
                                    SqlExpression<GXAmiTaskLog> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTaskLog>();
#endif                                                                
                                    ev.Where(q => q.UserID == id);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list.AddRange(Db.Select<GXAmiTaskLog>(ev).ToArray());
                                }
                                else
                                {
#if !SS4
                                    SqlExpressionVisitor<GXAmiTask> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiTask>();
#else
                                    SqlExpression<GXAmiTask> ev = OrmLiteConfig.DialectProvider.SqlExpression<GXAmiTask>();
#endif
                                    ev.Where(q => q.UserID == id);
                                    if (request.Descending)
                                    {
                                        ev.OrderByDescending(q => q.Id);
                                    }
                                    list = Db.Select<GXAmiTask>(ev);
                                }
                                
                            }
                        }
                    }
                }
                else //Get DC tasks.
                {
                    Guid guid = new Guid(s.UserAuthName);
                    //Get device tasks for the DC.
                    string query = "SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.TargetDeviceID = {1}.ID " +
                                                "INNER JOIN {2} ON {1}.ID = {2}.DeviceID " +
                                                "INNER JOIN {3} ON ({2}.DataCollectorID IS NULL OR {2}.DataCollectorID = {3}.ID) " +
                                                "WHERE ({0}.DataCollectorGuid IS NULL OR {0}.DataCollectorGuid = '{4}') AND {3}.Guid = '{4}'";
                    if (request.State != TaskState.All)
                    {
                        query += string.Format("AND {0}.State = {1}",
                                            GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db),
                                            (int) request.State);
                    }
                    query = string.Format(query,
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                                s.UserAuthName.Replace("-", ""));
                    list = Db.Select<GXAmiTask>(query);                    
                }
                //Remove excluded users.
                if (request.Excluded != null && request.Excluded.Length != 0)
                {
                    List<ulong> ids = new List<ulong>(request.Excluded);
                    var excludeUsers = from c in list where !ids.Contains(c.Id) select c;
                    list = excludeUsers.ToList();
                }
                //Get users by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > list.Count)
                    {
                        request.Count = list.Count - request.Index;
                    }
                    list.RemoveRange(0, request.Index);
                    var limitUsers = list.Take(request.Count);
                    list = limitUsers.ToList();
                }

                foreach (GXAmiTask it in list)
                {
                    it.Data = GetData(it.Id, request.Log);
                    UpdateTaskTarget(it);
                }
                return new GXTasksResponse(list.ToArray());
            }
        }

        string GetData(ulong taskId, bool log)
        {
            List<GXAmiTaskData> data;
            if (log)
            {
                data = new List<GXAmiTaskData>();
                data.AddRange(Db.Select<GXAmiTaskLogData>(q => q.TaskId == taskId).ToArray());
            }
            else
            {
                data = Db.Select<GXAmiTaskData>(q => q.TaskId == taskId);
            }
            if (data.Count == 0)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            foreach (GXAmiTaskData d in data)
            {
                sb.Append(d.Data);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Delete task.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTaskDeleteResponse Post(GXTaskDeleteRequest request)
        {
            long id = 0;
            List<GXAmiTask> list = new List<GXAmiTask>();
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                //If failed task adder id DC.
                bool superAdmin = false;
                Guid dcGuid = Guid.Empty;
                if (long.TryParse(s.Id, out id))
                {
                    //Is limited access.
                    if (IsLimitedAccess(s))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                }
                else //Adder is DC.
                {
                    if (!GXBasicAuthProvider.IsGuid(s.UserAuthName, out dcGuid))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    if (Db.Select<GXAmiDataCollector>(q => q.Guid == dcGuid).Count != 1)
                    {
                        throw new ArgumentException("Access denied.");
                    }
                }
                if (request.TaskIDs != null)
                {
                    foreach (ulong it in request.TaskIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("Invalid Task ID.");
                        }
#if !SS4
                        GXAmiTask task = Db.QueryById<GXAmiTask>(it);
#else
                        GXAmiTask task = Db.SingleById<GXAmiTask>(it);
#endif                       
                        //Task might be null if it removed.
                        if (task != null)
                        {
                            //Only task creator or super admin can remove tasks.                    
                            if (!superAdmin)
                            {
                                if (task != null)
                                {
                                    if (task.UserID != id && dcGuid != task.DataCollectorGuid)
                                    {
                                        throw new ArgumentException("Access denied. Only task creator or super admin can remove created tasks.");
                                    }
                                }
                            }
                            list.Add(task);
                        }
                    }
                }
                //Delete tasks from the user.
                if (request.UserIDs != null)
                {
                    foreach (long uid in request.UserIDs)
                    {
                        //Only task creator or super admin can remove tasks.                    
                        if (!superAdmin)
                        {
                            if (uid != id)
                            {
                                throw new ArgumentException("Access denied. Only task creator or super admin can remove created tasks.");
                            }
                        }
                        string query = string.Format("SELECT * FROM {0} WHERE UserID = {1}",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db), uid);
                        list.AddRange(Db.Select<GXAmiTask>(query));
                    }
                }
                //Delete tasks from the user groups.
                if (request.UserGroupIDs != null)
                {
                    foreach (long ugid in request.UserGroupIDs)
                    {
                        List<GXAmiUser> users = GXUserService.GetUsers(s, Db, id, ugid, false, true, null, SearchOperator.None, SearchType.All);
                        foreach (GXAmiUser user in users)
                        {
                            //Only task creator or super admin can remove tasks.                    
                            if (!superAdmin)
                            {
                                if (user.Id != id)
                                {
                                    throw new ArgumentException("Access denied. Only task creator or super admin can access task.");
                                }
                            }
                            string query = string.Format("SELECT * FROM {0} WHERE UserID = {1}",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db), user.Id);
                            list.AddRange(Db.Select<GXAmiTask>(query));
                        }
                    }
                }
                //Delete tasks from the device.
                if (request.DeviceIDs != null && request.DeviceIDs.Length != 0)
                {
                    foreach (ulong did in request.DeviceIDs)
                    {
                        list.AddRange(Db.Select<GXAmiTask>(q => q.TargetDeviceID == did));
                    }
                }

                //Delete tasks from the device groups.
                if (request.DeviceGroupIDs != null)
                {
                    //TODO:
                }
                if (request.DataCollectorIDs != null && request.DataCollectorIDs.Length != 0)
                {       
                    string query;
                    for(int pos = 0; pos != request.DataCollectorIDs.Length; ++pos)                    
                    {
                        if (string.IsNullOrEmpty(request.Names[pos]))
                        {
                            query = string.Format("SELECT * FROM {0} WHERE DataCollectorID = {1}",
                                    GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db), 
                                    request.DataCollectorIDs[pos]);
                        }
                        else
                        {
                            query = string.Format("SELECT * FROM {0} WHERE DataCollectorID = {1} AND 'Data' like('%{2}%')",
                                    GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db), 
                                    request.DataCollectorIDs[pos], 
                                    request.Names[pos]);
                        }
                        list.AddRange(Db.Select<GXAmiTask>(query));
                    }
                }
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {                    
                    foreach (GXAmiTask task in list)
                    {                        
                        task.State = TaskState.Succeeded;
                        events.Add(new GXEventsItem(ActionTargets.Task, Actions.Remove, task));
                    }
                    Db.Delete<GXAmiTask>(list.ToArray());
                    trans.Commit();
                }
            }
            //Notify that task is removed.
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXTaskDeleteResponse(list.ToArray());
        }
    }
}
