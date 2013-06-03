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
using ServiceStack.ServiceInterface;
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Data;
using ServiceStack.OrmLite;
using System;
using System.Xml.Linq;
using ServiceStack.ServiceInterface.Auth;
using GuruxAMI.Server;
using System.Text;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles Task functionality.
    /// </summary>
    [Authenticate]
    internal class GXTaskService : ServiceStack.ServiceInterface.Service
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
            lock (Db)
            {
                List<GXEventsItem> events = new List<GXEventsItem>();
                IAuthSession s = this.GetSession(false);
                long adderId = 0;
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
                    if (it.Id != 0)
                    {
                        throw new ArgumentException("Invalid target.");
                    }
                    if (it.TargetID == 0)
                    {
                        if (it.DataCollectorGuid == Guid.Empty || (it.TaskType != TaskType.MediaOpen &&
                            it.TaskType != TaskType.MediaClose &&
                            it.TaskType != TaskType.MediaWrite &&
                            it.TaskType != TaskType.MediaError &&
                            it.TaskType != TaskType.MediaState))
                        {
                            throw new ArgumentException("Invalid target.");
                        }
                        //Get DC ID
                        List<GXAmiDataCollector> tmp = Db.Select<GXAmiDataCollector>(q => q.Guid == it.DataCollectorGuid);
                        if (tmp.Count != 1)
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        it.TargetID = tmp[0].Id;
                    }
                    //TODO: Check that user has access to the target.                
                    it.State = TaskState.Pending;
                    it.UserID = adderId;
                    it.CreationTime = DateTime.Now;
                    it.SenderDataCollectorGuid = dcGuid;
                    events.Add(new GXEventsItem(ActionTargets.Task, Actions.Add, it));
                    /*
                    //If DC is added task.
                    if (adderId == 0)
                    {
                        it.SenderDataCollectorGuid = dcGuid;
                        events.Add(new GXEventsItem(ActionTargets.Task, Actions.Add, it));
                        using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                        {
                            //Generate new ID here.
                            it.Id = (int)Settings.GetNewTaskID(Db);
                            trans.Commit();
                        }
                    }
                    else //If user wants to read device.
                    {
                        //Get list of DCs who knows the meter.
                        string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.DataCollectorID WHERE {1}.DeviceID = {2}",
                            GuruxAMI.Server.AppHost.GetTableName<DataCollector>(Db),
                            GuruxAMI.Server.AppHost.GetTableName<DataCollectorDevice>(Db),
                            it.TargetDeviceID);
                        List<DataCollector> list = Db.Select<DataCollector>(query);
                        //Notify all data collectors that there is a new task available.
                        using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                        {
                            foreach (DataCollector collector in list)
                            {
                                Task task = it.Clone();
                                task.TargetDeviceID = it.TargetDeviceID;
                                it.TargetID = it.TargetID;
                                events.Add(new GXEventsItem(ActionTargets.Task, Actions.Add, task));
                                //Generate new ID here.
                                task.Id = (int)Settings.GetNewTaskID(Db);

                            }
                            trans.Commit();
                        }
                    }
                     * */
                }
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    foreach (GXAmiTask it in request.Tasks)
                    {
                        Db.Insert(it);
                        it.Id = (ulong)Db.GetLastInsertId();
                        if (it.Data != null)
                        {
                            int index = 0;
                            foreach (string value in SplitByLength(it.Data, 255))
                            {
                                GXAmiTaskData d = new GXAmiTaskData();
                                d.TaskId = it.Id;
                                d.Data = value;
                                d.Index = ++index;
                                Db.Insert(d);
                            }
                        }
                    }
                    trans.Commit();
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                return new GXTaskUpdateResponse(request.Tasks);
            }
        }

        static string[] SplitByLength(string str, int maxLength)
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
        /// Mark task(s) as claimed.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTasksClaimResponse Post(GXTasksClaimRequest request)
        {
            lock (Db)
            {
                List<GXEventsItem> events = new List<GXEventsItem>();
                IAuthSession s = this.GetSession(false);
                DateTime claimTime = DateTime.Now;
                Guid guid;
                if (!GXBasicAuthProvider.IsGuid(s.UserAuthName, out guid))
                {
                    throw new ArgumentException("Access denied.");
                }
                List<GXClaimedTask> infoList = new List<GXClaimedTask>();
                foreach (ulong it in request.TaskIDs)
                {
                    GXAmiDataCollector collector = null;
                    List<GXAmiTask> tasks = Db.Select<GXAmiTask>(q => q.Id == it);
                    //Make sure that no one else is has not claim the task.
                    if (tasks.Count == 1)
                    {
                        GXAmiTask task = Db.GetById<GXAmiTask>(it);
                        task.Data = GetData(it);
                        //If DC is clamiming the task for it self.
                        if (task.TargetDeviceID == 0 && task.DataCollectorGuid != Guid.Empty)
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
                            string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.DataCollectorID WHERE DeviceID = {2}",
                                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorDevice>(Db),
                                        task.TargetDeviceID);
                            List<GXAmiDataCollector> collectors = Db.Select<GXAmiDataCollector>(query);
                            foreach (GXAmiDataCollector dc in collectors)
                            {
                                if (dc.Guid == guid)
                                {
                                    collector = dc;
                                    break;
                                }
                            }
                            //If DC can't access task.
                            if (collector == null)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                        }
                        //Find device template Guid.
                        if (task.TargetDeviceID != 0)
                        {
                            string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.TemplateId WHERE {1}.ID = {2}",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                task.TargetDeviceID);
                            List<GXAmiDeviceTemplate> templates = Db.Select<GXAmiDeviceTemplate>(query);
                            if (templates.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            GXClaimedTask info = new GXClaimedTask();
                            info.DeviceTemplate = templates[0].Guid;
                            query = string.Format("SELECT {0}.* FROM {0} WHERE {0}.ID = {1}",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                task.TargetDeviceID);
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
                            info.Device.TemplateGuid = templates[0].Guid;
                            info.Media = devices[0].MediaName;
                            info.Settings = devices[0].MediaSettings;
                            info.Data = task.Data;
                            infoList.Add(info);
                        }
                        else //DC is claim the task to itself.
                        {
                            GXClaimedTask info = new GXClaimedTask();
                            if (task.TargetType == TargetType.Media)
                            {
                                if (task.TaskType == TaskType.MediaOpen ||
                                    task.TaskType == TaskType.MediaClose)
                                {
                                    string[] data = task.Data.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                    info.Media = data[0];
                                    info.Settings = data[1];
                                    task.Data = null;
                                }
                                else if (task.TaskType == TaskType.MediaWrite)
                                {
                                    string[] data = task.Data.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                    info.Media = data[0];
                                    info.Settings = data[1];
                                    task.Data = data[2];
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
                //Notify that task is claimed.
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, 0, events);
                return new GXTasksClaimResponse(infoList.ToArray());
            }
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
                    if (request.TaskIDs != null)
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
                                GXAmiTask task = Db.QueryById<GXAmiTask>(it);
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
                    else if (request.UserIDs != null)
                    {
                        foreach (long uid in request.UserIDs)
                        {
                            //Only task creator or super admin can remove tasks.                    
                            if (!superAdmin)
                            {
                                if (uid != id)
                                {
                                    throw new ArgumentException("Access denied. Only task creator or super admin can access task.");
                                }
                            }
                            if (request.State != TaskState.All)
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.UserID == uid && q.State == request.State));
                            }
                            else
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.UserID == uid));
                            }
                        }
                    }
                    //Get tasks by user group ID.
                    else if (request.UserGroupIDs != null)
                    {
                        foreach (long ugid in request.UserGroupIDs)
                        {
                            List<GXAmiUser> users = GXUserService.GetUsers(s, Db, id, ugid, false, true);
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
                                    list.AddRange(Db.Select<GXAmiTask>(q => q.UserID == user.Id && q.State == request.State));
                                }
                                else
                                {
                                    list.AddRange(Db.Select<GXAmiTask>(q => q.UserID == user.Id));
                                }
                            }
                        }
                    }
                    //Get tasks by device ID.
                    else if (request.DeviceIDs != null)
                    {
                        foreach (ulong dId in request.DeviceIDs)
                        {
                            if (GXDeviceService.GetDevices(s, Db, id, 0, 0, dId, false).Count == 0)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            if (request.State != TaskState.All)
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.TargetDeviceID == dId && q.State == request.State));
                            }
                            else
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.TargetDeviceID == dId));
                            }
                        }
                    }
                    //Get tasks by device group ID.
                    else if (request.DeviceGroupIDs != null)
                    {
                        foreach (ulong dgId in request.DeviceGroupIDs)
                        {
                            if (!GXDeviceGroupService.CanUserAccessDeviceGroup(Db, id, dgId))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            if (request.State != TaskState.All)
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.TargetID == dgId && q.State == request.State));
                            }
                            else
                            {
                                list.AddRange(Db.Select<GXAmiTask>(q => q.TargetID == dgId));
                            }
                        }
                    }
                    else //Select all tasks.
                    {
                        if (superAdmin)
                        {
                            if (request.State != TaskState.All)
                            {
                                list = Db.Select<GXAmiTask>(q => q.State == request.State);
                            }
                            else
                            {
                                list = Db.Select<GXAmiTask>();
                            }
                        }
                        else
                        {
                            if (request.State != TaskState.All)
                            {
                                list = Db.Select<GXAmiTask>(q => q.UserID == id && q.State == request.State);
                            }
                            else
                            {
                                list = Db.Select<GXAmiTask>(q => q.UserID == id);
                            }
                        }
                    }
                }
                else //Get DC tasks.
                {
                    Guid guid = new Guid(s.UserAuthName);
                    //Get device tasks for the DC.
                    string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.TargetDeviceID = {1}.ID INNER JOIN {2} ON {1}.ID = {2}.DeviceID INNER JOIN {3} ON {2}.DataCollectorID = {3}.ID WHERE {3}.Guid = '{4}'",
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiTask>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorDevice>(Db),
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                                s.UserAuthName.Replace("-", ""));
                    list = Db.Select<GXAmiTask>(query);
                    //Get DC Tasks.
                    if (request.State != TaskState.All)
                    {
                        list.AddRange(Db.Select<GXAmiTask>(q => q.DataCollectorGuid == guid && q.State == request.State));
                    }
                    else
                    {
                        list.AddRange(Db.Select<GXAmiTask>(q => q.DataCollectorGuid == guid));
                    }
                }
                foreach (GXAmiTask it in list)
                {
                    it.Data = GetData(it.Id);
                }
                return new GXTasksResponse(list.ToArray());
            }
        }

        string GetData(ulong taskId)
        {
            List<GXAmiTaskData> data = Db.Select<GXAmiTaskData>(q => q.TaskId == taskId);
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
        public GXTaskDeleteResponse Delete(GXTaskDeleteRequest request)
        {
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                long id = 0;
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
                List<GXAmiTask> list = new List<GXAmiTask>();
                List<GXEventsItem> events = new List<GXEventsItem>();
                if (request.TaskIDs != null)
                {
                    foreach (ulong it in request.TaskIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("Invalid Task ID.");
                        }
                        GXAmiTask task = Db.QueryById<GXAmiTask>(it);
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
                            task.State = TaskState.Succeeded;
                            events.Add(new GXEventsItem(ActionTargets.Task, Actions.Remove, task));
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
                        list = Db.Select<GXAmiTask>(query);
                        foreach (GXAmiTask task in list)
                        {
                            events.Add(new GXEventsItem(ActionTargets.Task, Actions.Remove, task));
                        }
                    }
                }
                //Delete tasks from the user groups.
                if (request.UserGroupIDs != null)
                {
                    foreach (long ugid in request.UserGroupIDs)
                    {
                        List<GXAmiUser> users = GXUserService.GetUsers(s, Db, id, ugid, false, true);
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
                            list = Db.Select<GXAmiTask>(query);
                            foreach (GXAmiTask task in list)
                            {
                                events.Add(new GXEventsItem(ActionTargets.Task, Actions.Remove, task));
                            }
                        }
                    }
                }
                //Delete tasks from the device.
                if (request.UserIDs != null)
                {
                }

                //Delete tasks from the device groups.
                if (request.DeviceGroupIDs != null)
                {
                }
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    Db.DeleteAll(list.ToArray());
                    foreach (GXAmiTask it in list)
                    {
                        Db.Delete<GXAmiTaskData>(q => q.TaskId == it.Id);
                    }
                    trans.Commit();
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, id, events);
                return new GXTaskDeleteResponse(list.ToArray());
            }
        }
    }
}
