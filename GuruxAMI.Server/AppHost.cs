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

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using GuruxAMI.Common;
using GuruxAMI.Common.Messages;
using GuruxAMI.Service;
using ServiceStack.OrmLite;
#if !SS4
using ServiceStack.ServiceHost;
using ServiceStack.Common.Web;
#else    
using ServiceStack.Web;
using ServiceStack;
#endif

namespace GuruxAMI.Server
{    
    class AppHost
    {
#if !SS4                
        /// <summary>
        /// Get IP address of the client.
        /// </summary>
        /// <param name="con"></param>
        /// <returns></returns>
        public static string GetIPAddress(IRequestContext con)
        {
            string add = null;
            var httpReq = con.Get<ServiceStack.ServiceHost.IHttpRequest>();
            //If proxy is used.
            if (httpReq != null)
            {
                add = httpReq.Headers[ServiceStack.Common.Web.HttpHeaders.XForwardedFor];
            }
            //Get IP Address.
            if (add == null)
            {
                add = con.Get<ServiceStack.ServiceHost.IHttpRequest>().UserHostAddress;
            }
            return add;
        }                
#endif
        
        /// <summary>
        /// 
        /// </summary>
        internal List<GXSession> Sessions = new List<GXSession>();

        GXSession GetSession(Guid session)
        {
            foreach (GXSession it in Sessions)
            {
                if (it.Session == session)
                {
                    return it;
                }
            }
            return null;
        }

        public void RemoveEvent(Guid listenerGuid, Guid guid)
        {
            lock (Sessions)
            {
                GXSession session = GetSession(listenerGuid);
                if (session != null)
                {
                    //Check that we are not listen this client.
                    foreach (GXEvent it in session.NotifyClients)
                    {
                        if (it.DataCollectorGuid == guid)
                        {
                            session.NotifyClients.Remove(it);
                            break;
                        }
                    }
                    //Remove collection if empty.
                    if (session.NotifyClients.Count == 0)
                    {
                        session.Received.Set();
                        Sessions.Remove(session);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RemoveEvent failed. Event not found.");
                }
            }
        }

        /// <summary>
        /// TODO: Remove event if not called after a hour.
        /// </summary>
        /// <param name="listenerGuid"></param>
        /// <param name="e"></param>
        public void AddEvent(Guid listenerGuid, GXEvent e)
        {
            lock (Sessions)
            {
                GXSession session = GetSession(listenerGuid);
                if (session != null)
                {
                    //Check that we are not listen this client.
                    foreach (GXEvent e1 in session.NotifyClients)
                    {
                        if (e1.Instance == e.Instance)
                        {
                            session.NotifyClients.Remove(e1);
                            break;
                        }
                    }
                    session.NotifyClients.Add(e);
                }
                else
                {
                    GXSession ses = new GXSession(e.Instance);
                    Sessions.Add(ses);
                    ses.NotifyClients.Add(e);
                }
            }
        }

        /// <summary>
        /// Check is DC already registered.
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public bool IsDCRegistered(Guid guid)
        {
            //If this is user, not DC.
            if (guid == Guid.Empty)
            {
                return false;
            }
            lock (Sessions)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e in it.NotifyClients)
                    {
                        if (e.DataCollectorGuid == guid)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public GuruxAMI.Common.Messages.GXEventsItem[] WaitEvents(IDbConnection Db, Guid listenerGuid, out Guid guid)
        {
            //Wait until event occurs.            
            GXSession session;
            lock (Sessions)
            {
                session = GetSession(listenerGuid);
                //If session not found. This is happening when DC is connected to the server and
                //Server is restarted.
                if (session == null)
                {
                    guid = Guid.Empty;
                    return new GuruxAMI.Common.Messages.GXEventsItem[0];
                    //TODO: Check is this better. throw new System.Net.WebException("", System.Net.WebExceptionStatus.RequestCanceled);
                }
                session.Received.Set();
                //Update DC last request time stamp.
                foreach (GXEvent it in session.NotifyClients)
                {
                    if (it.DataCollectorGuid != Guid.Empty)
                    {
                        lock (Db)
                        {
                            GXAmiDataCollector item = Db.Select<GXAmiDataCollector>(q => q.Guid == it.DataCollectorGuid)[0];
                            item.LastRequestTimeStamp = DateTime.Now.ToUniversalTime();
                            Db.UpdateOnly(item, p => p.LastRequestTimeStamp, p => p.Guid == it.DataCollectorGuid);
                        }
                    }
                }

                //Search if there are new events that are occurred when client is executed previous action.
                bool found = false;
                foreach (GXEvent it in session.NotifyClients)
                {
                    if (it.Rows.Count != 0)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Thread.Sleep(1);
                    session.Received.Reset();
                }
            }
            session.Received.WaitOne();
            lock (Sessions)
            {
                //Search occurred event.
                foreach (GXEvent it in session.NotifyClients)
                {
                    if (it.Rows.Count != 0)
                    {
                        guid = it.Instance;
                        GuruxAMI.Common.Messages.GXEventsItem[] rows = it.Rows.ToArray();
                        it.Rows.Clear();
                        return rows;
                    }
                }
            }
            guid = Guid.Empty;
            return new GuruxAMI.Common.Messages.GXEventsItem[0];
        }

        /// <summary>
        /// Handle property value updates.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="deviceId"></param>
        /// <param name="value"></param>
        /// <param name="e"></param>
        void HandleValueUpdated(IDbConnection Db, GXAmiLatestValue value, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this user event.
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin device owners
                        if (e1.SuperAdmin)//TODO: || e1.UserID == user.Id)
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        void HandleTableValuesUpdated(IDbConnection Db, GXAmiDataRow[] value, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this user event.            
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin device owners
                        if (e1.SuperAdmin)//TODO: || e1.UserID == user.Id)
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }
        void HandleUser(GXAmiUser user, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this user event.            
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin and user from the task.
                        if (e1.SuperAdmin || e1.UserID == user.Id)
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        void HandleUserGroup(IDbConnection Db, GXAmiUserGroup group, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this user group event.
            lock (Db)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e1 in it.NotifyClients)
                    {
                        if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                        {
                            //Notify only super admin and if user has access to the user group.
                            if (e1.SuperAdmin || GXUserGroupService.CanAccess(Db, e1.UserID, group.Id))
                            {
                                e1.Rows.Add(e);
                                it.Received.Set();
                            }
                        }
                    }
                }
            }
        }

        void HandleDevice(IDbConnection Db, ulong deviceId, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device event.            
            lock (Db)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e1 in it.NotifyClients)
                    {
                        if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                        {
                            //Notify only super admin and if user has access to this device.
                            if (e1.SuperAdmin || GXDeviceService.CanUserAccessDevice(Db, e1.UserID, deviceId))
                            {
                                e1.Rows.Add(e);
                                it.Received.Set();
                            }
                        }
                        else if (e1.DataCollectorGuid != Guid.Empty && GXDataCollectorService.CanDataCollectorsAccessDevice(Db, deviceId, e1.DataCollectorGuid))
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        void HandleDeviceGroup(IDbConnection Db, GXAmiDeviceGroup group, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device group event.            
            lock (Db)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e1 in it.NotifyClients)
                    {
                        if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                        {
                            //Notify only super admin and if user has access to this device group.
                            if (e1.SuperAdmin || GXDeviceGroupService.CanUserAccessDeviceGroup(Db, e1.UserID, group.Id))
                            {
                                e1.Rows.Add(e);
                                it.Received.Set();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Notify DC if device is connected to the DC.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="task"></param>
        /// <param name="dataCollectorGuid"></param>
        /// <returns></returns>
        static bool IsDeviceConnectedToDC(IDbConnection Db, GXAmiTask task, Guid dataCollectorGuid)
        {
            if ((task.TargetType == TargetType.Device || task.TargetType == TargetType.Category ||
                task.TargetType == TargetType.Table || task.TargetType == TargetType.Property) &&
                dataCollectorGuid != Guid.Empty)
            {
                string query = string.Format("SELECT COUNT(*) FROM {0} WHERE Disabled = FALSE AND DeviceID = {1} AND (DataCollectorId = {2} OR DataCollectorId IS NULL)",
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                    task.TargetDeviceID,
                    task.DataCollectorID);
                lock (Db)
                {
                    return Db.SqlScalar<long>(query, null) != 0;
                }
            }
            return false;
        }

        void HandleDataCollectors(IDbConnection Db, Guid guid, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this data collector event.            
            lock (Db)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e1 in it.NotifyClients)
                    {
                        if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                        {
                            //Notify only super admin or if user has access to this data collector.
                            if (e1.SuperAdmin)//TODO: || GXDeviceGroupService.CanUserAccessDeviceGroup(Db, e1.UserID, group.Id))
                            {
                                e1.Rows.Add(e);
                                it.Received.Set();
                            }
                        }
                        //Notify DC that trace state is change.
                        else if (e.Action == Actions.Edit && e.Target == ActionTargets.Trace &&
                                e1.DataCollectorGuid == guid)
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        void HandleSchedules(GXAmiSchedule schedule, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this data collector event.            
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin or if user has access to this data collector.
                        if (e1.SuperAdmin)//TODO: || GXDeviceGroupService.CanUserAccessDeviceGroup(Db, e1.UserID, group.Id))
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                        /* TODO:
                    //Notify DC that Schedule is added.
                    else if (e.Action == Actions.Edit && e.Target == ActionTargets.Trace &&
                            e1.DataCollectorGuid == schedule.datac)
                    {
                        e1.Rows.Add(e);
                        it.Received.Set();
                    }
                         * */
                }
            }
        }

        void HandleDeviceErrors(IDbConnection Db, GXAmiDeviceError error, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this data collector event.            
            lock (Db)
            {
                foreach (GXSession it in Sessions)
                {
                    foreach (GXEvent e1 in it.NotifyClients)
                    {
                        if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                        {
                            //Notify only super admin or if user has access to this data collector.
                            if (e1.SuperAdmin || GXDeviceService.CanUserAccessDevice(Db, e1.UserID, error.TargetDeviceID.Value))
                            {
                                e1.Rows.Add(e);
                                it.Received.Set();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Notify from new device profile.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="template"></param>
        /// <param name="e"></param>
        void HandleDeviceProfiles(GXAmiDeviceProfile template, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device template event.            
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin from new templates.
                        if (e1.SuperAdmin)
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Notify from new device tasks.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="task"></param>
        /// <param name="e"></param>
        void HandleTasks(IDbConnection Db, GXAmiTask task, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            lock (Db)
            {
                if (task.State == TaskState.Pending)
                {
                    GXAmiTaskLog it = new GXAmiTaskLog(task);
                    Db.Insert<GXAmiTaskLog>(it);
                    if (it.Data != null)
                    {
                        foreach (string value in GXTaskService.SplitByLength(it.Data, 255))
                        {
                            GXAmiTaskLogData d = new GXAmiTaskLogData();
                            d.TaskId = it.Id;
                            d.Data = value;
                            Db.Insert(d);
                        }
                    }
                }
                //DC is claimed the task.
                else if (task.State == TaskState.Processing)
                {
                    GXAmiTaskLog it = Db.GetById<GXAmiTaskLog>(task.Id);
                    it.ClaimTime = task.ClaimTime;
                    Db.UpdateOnly(it, p => p.ClaimTime, q => q.Id == task.Id);
                }
                else //Task is finished (succeeded or failed)
                {
                    GXAmiTaskLog it = Db.GetById<GXAmiTaskLog>(task.Id);
                    it.FinishTime = DateTime.Now.ToUniversalTime();
                    Db.UpdateOnly(it, p => p.FinishTime, q => q.Id == task.Id);                    
                }
            }
            foreach (GXSession it in Sessions)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if ((mask & e1.Mask) != 0)
                    {
                        //Do not notify task sender.
                        if (task.State == TaskState.Pending && e1.Instance == task.Instance)
                        {
                            continue;
                        }
                        //Do not notify sender DC.
                        if (task.State == TaskState.Processing && e1.DataCollectorGuid == task.DataCollectorGuid)
                        {
                            continue;
                        }
                        //Do not notify other DCs for task success or fail.
                        if ((task.State == TaskState.Succeeded ||
                            task.State == TaskState.Failed ||
                            task.State == TaskState.Timeout) &&
                            e1.DataCollectorGuid != Guid.Empty && 
                            e1.DataCollectorGuid != task.DataCollectorGuid)
                        {
                            continue;
                        }
                        //Notify only super admin, user and DC from the task.
                        if (e1.SuperAdmin || (e1.UserID != 0 && e1.UserID == task.UserID) ||
                            e1.DataCollectorGuid == task.DataCollectorGuid ||
                            //If device is read notify DCs that owns the device.
                            IsDeviceConnectedToDC(Db, task, e1.DataCollectorGuid))
                        {
                            if (e1.DataCollectorGuid != Guid.Empty)
                            {
                                System.Diagnostics.Debug.WriteLine("Server notifies: " + task.TaskType.ToString() + " " + task.State.ToString() + " DC: " + e1.DataCollectorGuid.ToString());
                            }
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

#if !SS4
        public void SetEvents(IDbConnection Db, IHttpRequest request, long userId, List<GXEventsItem> events)
#else
        public void SetEvents(IDbConnection Db, IRequest request, long userId, List<GXEventsItem> events)
#endif                
        {
            //If proxy is used.
            string add = request.Headers[HttpHeaders.XForwardedFor];
            //Get IP Address.
            if (add == null)
            {
                add = request.UserHostAddress;
            }
            lock (Sessions)
            {
                foreach (GXEventsItem e in events)
                {
                    if (userId != 0)
                    {
                        lock(Db)
                        {
                            GXAmiUserActionLog a = new GXAmiUserActionLog(userId, e.Target, e.Action, add);
                            Db.Insert(a);
                        }
                    }
                    if (e.Target == ActionTargets.User)
                    {
                        HandleUser(e.Data as GXAmiUser, e);
                    }
                    else if (e.Target == ActionTargets.UserGroup)
                    {
                        HandleUserGroup(Db, e.Data as GXAmiUserGroup, e);
                    }
                    else if (e.Target == ActionTargets.Device)
                    {
                        HandleDevice(Db, (e.Data as GXAmiDevice).Id, e);
                    }
                    else if (e.Target == ActionTargets.DeviceGroup)
                    {
                        HandleDeviceGroup(Db, e.Data as GXAmiDeviceGroup, e);
                    }
                    else if (e.Target == ActionTargets.Task)
                    {
                        HandleTasks(Db, e.Data as GXAmiTask, e);
                    }
                    else if (e.Target == ActionTargets.DataCollector)
                    {
                        HandleDataCollectors(Db, (e.Data as GXAmiDataCollector).Guid, e);
                    }
                    else if (e.Target == ActionTargets.ValueChanged)
                    {
                        HandleValueUpdated(Db, e.Data as GXAmiLatestValue, e);
                    }
                    else if (e.Target == ActionTargets.TableValueChanged)
                    {
                        HandleTableValuesUpdated(Db, e.Data as GXAmiDataRow[], e);
                    }
                    else if (e.Target == ActionTargets.DeviceError)
                    {
                        HandleDeviceErrors(Db, e.Data as GXAmiDeviceError, e);
                    }
                    else if (e.Target == ActionTargets.DeviceProfile)
                    {
                        HandleDeviceProfiles(e.Data as GXAmiDeviceProfile, e);
                    }
                    else if (e.Target == ActionTargets.Schedule)
                    {
                        HandleSchedules(e.Data as GXAmiSchedule, e);
                    }
                    else if (e.Target == ActionTargets.Trace)
                    {
                        if (e.Data is GXAmiTrace)
                        {
                            ulong id = (e.Data as GXAmiTrace).DeviceId;
                            if (id == 0)
                            {
                                HandleDataCollectors(Db, (e.Data as GXAmiTrace).DataCollectorGuid, e);
                            }
                            else
                            {
                                HandleDevice(Db, id, e);
                            }
                        }
                        else if (e.Data is GXAmiDataCollector)
                        {
                            HandleDataCollectors(Db, (e.Data as GXAmiDataCollector).Guid, e);
                        }
                        else if (e.Data is GXAmiDevice)
                        {
                            HandleDevice(Db, (e.Data as GXAmiDevice).Id, e);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Trace.Assert(false);
                    }
                }
            }
        }

        /// <summary>
        /// Get name of the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <returns></returns>
        public static string GetTableName<T>(IDbConnection db)
        {
            var modelDef = ModelDefinition<T>.Definition;
            return OrmLiteConfig.DialectProvider.GetQuotedTableName(modelDef);
        }

        public static string GetColumnNames<T>(IDbConnection db)
        {
            var modelDef = ModelDefinition<T>.Definition;
            return OrmLiteConfig.DialectProvider.GetColumnNames(modelDef);
        }
    }  	
}
