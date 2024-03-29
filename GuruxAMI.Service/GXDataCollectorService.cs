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
using GuruxAMI.Common;
using GuruxAMI.Common.Messages;
using ServiceStack.OrmLite;
using GuruxAMI.Server;
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
    [Authenticate]
    internal class GXDataCollectorService : GXService
    {
        /// <summary>
        /// Add new data collector.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDataCollectorUpdateResponse Put(GXDataCollectorUpdateRequest request)
        {
            GXAmiDataCollector[] collectors = request.Collectors;
            IAuthSession s = this.GetSession(false);
            long id;
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                string lastIP;
#if !SS4                
                lastIP = GuruxAMI.Server.AppHost.GetIPAddress(base.RequestContext);
#else
                lastIP = "";//TODO;
#endif                
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    if (long.TryParse(s.Id, out id))
                    {
                        //Normal user can't change add new data collectors.
                        if (!GuruxAMI.Server.GXBasicAuthProvider.CanEditDevice(s))
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                        //If user want's to add new data collectors.
                        if (request.Collectors != null)
                        {
                            foreach (GXAmiDataCollector collector in request.Collectors)
                            {
                                if (collector.Id == 0)
                                {
                                    collector.Guid = Guid.NewGuid();
                                    collector.Added = DateTime.Now.ToUniversalTime();
                                    Db.Insert(collector);
#if !SS4
                                    collector.Id = (ulong)Db.GetLastInsertId();
#else
                                    collector.Id = (ulong)Db.LastInsertId();
#endif                                   
                                    events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Add, collector));
                                }
                                else
                                {
                                    //If DC is added to user group it's state is no longer unassigned.
                                    bool assigned = false;
                                    if (collector.UnAssigned && request.UserGroupIDs != null)
                                    {
                                        events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Remove, collector.Clone()));
                                        assigned = true;
                                        collector.UnAssigned = false;
                                    }                                    
                                    Db.UpdateOnly(collector, p => p.MAC, p => p.Id == collector.Id);
                                    Db.UpdateOnly(collector, p => p.UnAssigned, p => p.Id == collector.Id);
                                    Db.UpdateOnly(collector, p => p.Name, p => p.Id == collector.Id);
                                    Db.UpdateOnly(collector, p => p.Description, p => p.Id == collector.Id);
                                    //If DC is assigned remove it first and then add like assigned.
                                    if (assigned)
                                    {
                                        events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Add, collector));
                                    }
                                    else
                                    {
                                        events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Edit, collector));
                                    }
                                }
                                //If data collector is added to device group.
                                if (request.UserGroupIDs != null)
                                {
                                    foreach (long it in request.UserGroupIDs)
                                    {
                                        //Can user access user group.
                                        if (!superAdmin && !GXUserGroupService.CanAccess(Db, id, it))
                                        {
                                            throw new ArgumentException("Access denied.");
                                        }
                                        //Check that DC is not already added to the user group.
                                        List<GXAmiDataCollectorUserGroup> dcu = Db.Select<GXAmiDataCollectorUserGroup>(q => q.DataCollectorID == collector.Id && q.UserGroupID == it);
                                        if (dcu.Count == 0)
                                        {
                                            GXAmiDataCollectorUserGroup u = new GXAmiDataCollectorUserGroup();
                                            u.DataCollectorID = collector.Id;
                                            u.UserGroupID = it;
                                            Db.Insert(u);
#if !SS4
                                            GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(it);
#else
                                            GXAmiUserGroup ug = Db.SingleById<GXAmiUserGroup>(it);
#endif                                           
                                            events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, ug));
                                        }
                                    }
                                }
                            }
                        }
                        //User is adding collector by MAC address.
                        else if (request.MacAddress != null)
                        {
                            GXAmiDataCollector it = new GXAmiDataCollector();
                            collectors = new GXAmiDataCollector[] { it };
                            it.Guid = Guid.NewGuid();
                            s.UserAuthName = it.Guid.ToString();
                            it.Added = DateTime.Now.ToUniversalTime();
                            it.IP = lastIP;
                            it.UnAssigned = true;
                            if (request.MacAddress != null)
                            {
                                it.MAC = MacToString(request.MacAddress);
                            }
                            Db.Insert(it);
#if !SS4
                            it.Id = (ulong)Db.GetLastInsertId();
#else
                            it.Id = (ulong)Db.LastInsertId();
#endif                            
                            events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Add, it));
                        }
                        else
                        {
                            throw new ArgumentNullException("MAC Address.");
                        }
                    }
                    else
                    {
                        //DC is updating itself.
                        if (request.MacAddress == null)
                        {
                            Guid guid;
                            if (!GuruxAMI.Server.GXBasicAuthProvider.IsGuid(s.Id, out guid))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            List<GXAmiDataCollector> tmp = Db.Select<GXAmiDataCollector>(q => q.Guid == guid);
                            if (tmp.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            foreach (GXAmiDataCollector col in request.Collectors)
                            {
                                col.IP = lastIP;
                                Db.Update(col);
                                collectors = request.Collectors;
                                events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Edit, col));
                            }
                        }
                        else
                        {
                            List<GXAmiDataCollector> tmp = Db.Select<GXAmiDataCollector>(q => q.MAC == MacToString(request.MacAddress));
                            //If data collector is adding itself.
                            //Check if DC is already try to add itself.                    
                            if (tmp.Count == 0)
                            {
                                GXAmiDataCollector it = new GXAmiDataCollector();
                                collectors = new GXAmiDataCollector[] { it };
                                it.Guid = Guid.NewGuid();
                                s.UserAuthName = it.Guid.ToString();
                                it.Added = DateTime.Now.ToUniversalTime();
                                it.IP = lastIP;
                                it.UnAssigned = true;
                                if (request.MacAddress != null)
                                {
                                    it.MAC = MacToString(request.MacAddress);
                                }
                                Db.Insert(it);
#if !SS4
                                it.Id = (ulong)Db.GetLastInsertId();
#else
                                it.Id = (ulong)Db.LastInsertId();
#endif                                
                                events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Add, it));
                            }
                            else
                            {
                                collectors = new GXAmiDataCollector[] { tmp[0] };
                            }
                        }
                    }
                    //Accept changes.
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXDataCollectorUpdateResponse(collectors);
        }

        string MacToString(byte[] address)
        {
            System.Text.StringBuilder mac = new System.Text.StringBuilder(20);
            foreach (byte bt in address)
            {
                if (mac.Length != 0)
                {
                    mac.Append(":");
                }                
                mac.Append(bt.ToString("X2"));
            }
            return mac.ToString();
        }

        static public List<GXAmiDataCollector> GetDataCollectorsByUser(IAuthSession s, IDbConnection Db, long userId, long userGroupId, bool removed, string[] search, SearchOperator searchOperator, SearchType searchType)
        {
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = string.Format("SELECT {0}.* FROM {0} ",
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db));
            if (!admin || userId != 0)
            {
                string tmp = "INNER JOIN {1} ON {0}.ID = {1}.DataCollectorID ";
                tmp += "INNER JOIN {2} ON {1}.UserGroupID = {2}.ID ";
                tmp += "INNER JOIN {3} ON {2}.ID = {3}.UserGroupID ";
                query += string.Format(tmp,
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));
                Filter.Add(string.Format("{0}.Removed IS NULL AND {1}.Removed IS NULL",
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db)));                
                if (userId != 0)
                {
                    Filter.Add("UserID = " + userId.ToString());
                }
                else if (!admin)
                {
                    Filter.Add("UserID = " + id.ToString());
                }
                if (userGroupId != 0)
                {
                    Filter.Add("UserGroupID = " + userGroupId.ToString());
                }
            }            
            if (!removed)
            {
                Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db) + ".Removed IS NULL");
            }
            if (search != null)
            {
                List<string> searching = new List<string>();
                //searchOperator, SearchType searchType
                foreach (string it in search)
                {
                    if ((searchType & SearchType.Name) != 0)
                    {
                        string tmp = string.Format("{0}.Name Like('%{1}%')",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db), it);
                        searching.Add(tmp);
                    }
                }
                if ((searchOperator & SearchOperator.And) != 0)
                {
                    Filter.Add("(" + string.Join(" AND ", searching.ToArray()) + ")");
                }
                if ((searchOperator & SearchOperator.Or) != 0)
                {
                    Filter.Add("(" + string.Join(" OR ", searching.ToArray()) + ")");
                }
            }
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }            
            return Db.Select<GXAmiDataCollector>(query);
        }

        static public bool CanDataCollectorsAccessDevice(IDbConnection Db, ulong deviceId, Guid dataCollectorGuid)
        {
            List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(p => p.Guid == dataCollectorGuid);
            if (list.Count != 1)
            {
                return false;
            }
            string query = string.Format("SELECT COUNT(*) FROM {0} WHERE Disabled = FALSE AND DeviceID = {1} AND (DataCollectorId = {2} OR DataCollectorId IS NULL)",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                            deviceId,
                            list[0].Id);
            return Db.SqlScalar<long>(query, null) != 0;
        }

        static public List<GXAmiDataCollector> GetDataCollectorsByDevice(IAuthSession s, IDbConnection Db, ulong deviceID, ulong deviceGroupId, bool removed)
        {
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = string.Format("SELECT {0}.* FROM {0} ",
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db));
            if (!admin || deviceID != 0)
            {
                string tmp = "INNER JOIN {1} ON ({0}.ID = {1}.DataCollectorID OR {1}.DataCollectorID IS NULL)";
                tmp += "INNER JOIN {2} ON {1}.DeviceID = {2}.ID ";                
                if (!admin)
                {
                    tmp += "INNER JOIN {3} ON {2}.ID = {3}.DeviceID ";
                    tmp += "LEFT JOIN {4} ON {3}.DeviceGroupID = {4}.ID ";
                    tmp += "LEFT JOIN {5} ON {4}.ID = {5}.UserGroupID ";
                    tmp += "LEFT JOIN {6} ON {5}.UserGroupID = {6}.ID ";
                    tmp += "LEFT JOIN {7} ON {6}.ID = {7}.UserGroupID ";
                }
                query += string.Format(tmp,
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),                    
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroupDevice>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));

                if (!admin)
                {
                    Filter.Add(string.Format("{0}.Removed IS NULL AND {1}.Removed IS NULL AND {2}.Removed IS NULL",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db)));
                }
                else
                {
                    Filter.Add(string.Format("{0}.Removed IS NULL",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db)));                    
                }
                if (!admin)
                {
                    Filter.Add("UserID = " + id.ToString());
                }
                if (deviceGroupId != 0)
                {
                    Filter.Add("DeviceGroupID = " + deviceGroupId.ToString());
                }
            }
            if (!removed)            
            {
                Filter.Add(string.Format("{0}.Removed IS NULL", 
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db)));
            }
            Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db) + ".Disabled = FALSE");
            if (deviceID != 0)
            {
                Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db) + ".DeviceID = " + deviceID.ToString());
            }
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }
            return Db.Select<GXAmiDataCollector>(query);
        }

        /// <summary>
        /// Get data collectors.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDataCollectorsResponse Post(GXDataCollectorsRequest request)
        {
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                long id = 0;
                List<GXAmiDataCollector> list = new List<GXAmiDataCollector>();
                if (long.TryParse(s.Id, out id))
                {
                    if (request.DataCollectorId != 0)
                    {
                        list = Db.Select<GXAmiDataCollector>(q => q.Id == request.DataCollectorId);
                    }
                    //Return all unassigned data controllers.
                    else if (request.UnAssigned)
                    {
                        string query = string.Format("SELECT * FROM {0} WHERE Removed IS NULL AND UnAssigned = TRUE",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db));
                        list = Db.Select<GXAmiDataCollector>(query);
                    }
                    //Return all data controllers that Device can access.
                    else if (request.DeviceId != 0)
                    {
                        list = GetDataCollectorsByDevice(s, Db, request.DeviceId, 0, request.Removed);
                    }
                    //Return all data controllers by mac address.
                    else if (request.MacAddress != null)
                    {
                        list = Db.Select<GXAmiDataCollector>(q => q.MAC == MacToString(request.MacAddress).Replace(":", ""));
                    }
                    else if (request.IPAddress != null)
                    {
                        list = Db.Select<GXAmiDataCollector>(q => q.IP == request.IPAddress);
                    }
                    else if (request.Guid != Guid.Empty)
                    {
                        list = Db.Select<GXAmiDataCollector>(q => q.Guid == request.Guid);
                    }
                    else //Return all data contollers that user can access.
                    {
                        list = GetDataCollectorsByUser(s, Db, request.UserId, 0, request.Removed, null, SearchOperator.None, SearchType.All);
                    }
                }
                else //DC asks available DCs.
                {
                    list = Db.Select<GXAmiDataCollector>(q => q.Guid == new Guid(s.UserAuthName));
                }

                //Remove excluded data collectors.
                if (request.Excluded != null && request.Excluded.Length != 0)
                {
                    List<ulong> ids = new List<ulong>(request.Excluded);
                    var excludeUserGroups = from c in list where !ids.Contains(c.Id) select c;
                    list = excludeUserGroups.ToList();
                }
                //Get data collectors by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > list.Count)
                    {
                        request.Count = list.Count - request.Index;
                    }
                    list.RemoveRange(0, request.Index);
                    var limitUserGroups = list.Take(request.Count);
                    list = limitUserGroups.ToList();
                }
                //Change date time from Universal time to local time
                foreach (GXAmiDataCollector it in list)
                {
                    if (it.LastRequestTimeStamp.HasValue)
                    {
                        it.LastRequestTimeStamp = it.LastRequestTimeStamp.Value.ToLocalTime();
                    }
                }
                return new GXDataCollectorsResponse(list.ToArray());
            }
        }

        /// <summary>
        /// Delete data collector.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDataCollectorDeleteResponse Post(GXDataCollectorDeleteRequest request)
        {
            IAuthSession s = this.GetSession(false);
            long id = 0;
            List<GXAmiDataCollector> list = new List<GXAmiDataCollector>();
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                if (!long.TryParse(s.Id, out id))
                {
                    throw new ArgumentException("Access denied.");
                }
                //Normal user can't change add new data collectors.
                if (!GuruxAMI.Server.GXBasicAuthProvider.CanEditDevice(s))
                {
                    throw new ArgumentException("Access denied.");
                }
                bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                if (request.DataCollectorIDs != null)
                {
                    //TODO: check that user can't remove DC that he do not have access.
                    foreach (ulong it in request.DataCollectorIDs)
                    {
                        List<GXAmiDataCollector> item = Db.Select<GXAmiDataCollector>(q => q.Id == it);
                        if (request.Permanently)
                        {
                            Db.Delete(item[0]);
                        }
                        else
                        {
                            if (item.Count != 1)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            else
                            {
                                item[0].Removed = DateTime.Now.ToUniversalTime();
                                Db.UpdateOnly(item[0], p => p.Removed, p => p.Id == it);
                            }
                        }
                        events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.Remove, item[0]));
                    }
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXDataCollectorDeleteResponse();
        }

        /// <summary>
        /// Update new data collector state.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDataCollectorStateUpdateResponse Put(GXDataCollectorStateUpdateRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {                
                foreach (var it in request.States)
                {
#if !SS4
                    GXAmiDataCollector dc = Db.GetById<GXAmiDataCollector>(it.Key);
#else
                    GXAmiDataCollector dc = Db.SingleById<GXAmiDataCollector>(it.Key);                    
#endif                                                
                    dc.State = it.Value;
                    Db.UpdateOnly(dc, p => p.State, p => p.Id == it.Key);
                    events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.State, dc));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXDataCollectorStateUpdateResponse();
        }
    }
}
