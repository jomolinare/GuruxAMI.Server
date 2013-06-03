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
using ServiceStack.ServiceHost;

namespace GuruxAMI.Server
{    
    class AppHost
    {
        /// <summary>
        /// Get IP address of the client.
        /// </summary>
        /// <param name="con"></param>
        /// <returns></returns>
        public static string GetIPAddress(ServiceStack.ServiceHost.IRequestContext con)
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
        
        /// <summary>
        /// 
        /// </summary>
        internal List<GXSession> Events = new List<GXSession>();

        GXSession GetSession(Guid listenerGuid)
        {
            foreach (GXSession it in Events)
            {
                if (it.ListenerGuid == listenerGuid)
                {
                    return it;
                }
            }
            return null;
        }

        public void RemoveEvent(Guid listenerGuid, Guid guid)
        {
            lock (Events)
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
                        Events.Remove(session);
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
            lock (Events)
            {
                GXSession session = GetSession(listenerGuid);
                if (session != null)
                {
                    //Check that we are not listen this client.
                    foreach (GXEvent e1 in session.NotifyClients)
                    {
                        if (e1.DataCollectorGuid == e.DataCollectorGuid)
                        {
                            session.NotifyClients.Remove(e1);
                            break;
                        }
                    }
                    session.NotifyClients.Add(e);
                }
                else
                {
                    GXSession ses = new GXSession(listenerGuid);
                    Events.Add(ses);
                    ses.NotifyClients.Add(e);
                }
            }
        }

        public GuruxAMI.Common.Messages.GXEventsItem[] WaitEvents(Guid listenerGuid, out Guid guid)
        {
            //Wait until event occurs.            
            GXSession session;
            lock (Events)
            {
                session = GetSession(listenerGuid);
                //If session not found. This is happening when DC is connected to the server and
                //Server is restarted.
                if (session == null)
                {
                    throw new System.Net.WebException("", System.Net.WebExceptionStatus.RequestCanceled);
                }
                session.Received.Set();
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
            lock (Events)
            {
                //Search occurred event.
                foreach (GXEvent it in session.NotifyClients)
                {
                    if (it.Rows.Count != 0)
                    {
                        guid = it.DataCollectorGuid;
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
            foreach (GXSession it in Events)
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
            foreach (GXSession it in Events)
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
        void HandleUser(IDbConnection Db, GXAmiUser user, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this user event.            
            foreach (GXSession it in Events)
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
            foreach (GXSession it in Events)
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

        void HandleDevice(IDbConnection Db, ulong deviceId, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device event.            
            foreach (GXSession it in Events)
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

        void HandleDeviceGroup(IDbConnection Db, GXAmiDeviceGroup group, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device group event.            
            foreach (GXSession it in Events)
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
                string query = string.Format("SELECT DataCollector.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.DataCollectorID WHERE {1}.DeviceID = {2} AND Guid = \"{3}\" ",
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorDevice>(Db),
                    task.TargetDeviceID, dataCollectorGuid.ToString().Replace("-", ""));
                List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(query);
                return list.Count == 1;
            }
            return false;
        }

        void HandleDataCollectors(IDbConnection Db, Guid guid, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this data collector event.            
            foreach (GXSession it in Events)
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


        void HandleDeviceErrors(IDbConnection Db, GXAmiDeviceError error, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this data collector event.            
            foreach (GXSession it in Events)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if (e1.UserID != 0 && (mask & e1.Mask) != 0)
                    {
                        //Notify only super admin or if user has access to this data collector.
                        if (e1.SuperAdmin || GXDeviceService.CanUserAccessDevice(Db, e1.UserID, error.TargetDeviceID))
                        {
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Notify from new device templates.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="template"></param>
        /// <param name="e"></param>
        void HandleDeviceTemplates(IDbConnection Db, GXAmiDeviceTemplate template, GXEventsItem e)
        {
            ulong mask = (ulong)((int)e.Target << 16 | (int)e.Action);
            //Find is anyone interested from this device template event.            
            foreach (GXSession it in Events)
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
            Db.Insert<GXAmiTaskLog>(new GXAmiTaskLog(task));
            foreach (GXSession it in Events)
            {
                foreach (GXEvent e1 in it.NotifyClients)
                {
                    if ((mask & e1.Mask) != 0)
                    {
                        //Tark modifier DC is not notified.
                        if (task.SenderDataCollectorGuid != Guid.Empty &&
                            e1.DataCollectorGuid == task.SenderDataCollectorGuid)
                        {
                            continue;
                        }
                        //Notify only super admin, user and DC from the task.
                        if (e1.SuperAdmin || (e1.UserID != 0 && e1.UserID == task.UserID) ||
                            e1.DataCollectorGuid == task.DataCollectorGuid ||
                            //If device is read notify DCs that owns the device.
                            IsDeviceConnectedToDC(Db, task, e1.DataCollectorGuid))
                        {
                            if (e1.UserID != 0)
                            {
                                System.Diagnostics.Debug.WriteLine("Notify user " + e1.UserID.ToString() + " from task : " + task.Id.ToString());
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("Notify DC " + e1.DataCollectorGuid.ToString() + " from task : " + task.Id.ToString());
                            }
                            e1.Rows.Add(e);
                            it.Received.Set();
                        }
                    }
                }
            }
        }

        public void SetEvents(IDbConnection Db, IHttpRequest request, long userId, List<GXEventsItem> events)
        {
            string add = null;
            //If proxy is used.
            add = request.Headers[ServiceStack.Common.Web.HttpHeaders.XForwardedFor];
            //Get IP Address.
            if (add == null)
            {
                add = request.UserHostAddress;
            }
            lock (Events)
            {
                foreach (GXEventsItem e in events)
                {
                    if (userId != 0)
                    {
                        GXAmiUserActionLog a = new GXAmiUserActionLog(userId, e.Target, e.Action, add);
                        Db.Insert(a);
                    }
                    if (e.Target == ActionTargets.User)
                    {
                        HandleUser(Db, e.Data as GXAmiUser, e);
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
                    else if (e.Target == ActionTargets.DeviceTemplate)
                    {
                        HandleDeviceTemplates(Db, e.Data as GXAmiDeviceTemplate, e);
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
