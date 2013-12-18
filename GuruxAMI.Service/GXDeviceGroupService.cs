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
using System.Linq;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles device group functionality.
    /// </summary>
	[Authenticate]
    internal class GXDeviceGroupService : ServiceStack.ServiceInterface.Service
	{
        /// <summary>
        /// Add or update device group.
        /// </summary>
        public GXDeviceGroupUpdateResponse Put(GXDeviceGroupUpdateRequest request)       
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {                
                //Add new device groups           
                foreach (GXAmiDeviceGroup it in request.Items)
                {
                    if (string.IsNullOrEmpty(it.Name))
                    {
                        throw new ArgumentException("Invalid name.");
                    }
                    //If new device group.
                    if (it.Id == 0)
                    {
                        it.Id = GXAmiSettings.GetNewDeviceID(Db);
                        it.Added = DateTime.Now.ToUniversalTime();
                        Db.Insert(it);                        
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Add, it));                                               
                    }
                    else //Update device group.
                    {
                        //Get Added time.
                        GXAmiDeviceGroup orig = Db.GetById<GXAmiDeviceGroup>(it.Id);
                        it.Added = orig.Added;
                        Db.Update(it);
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, it));                                                                                        
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }            
            return new GXDeviceGroupUpdateResponse(request.Items);
        }

        /// <summary>
        /// //Add devices to device groups.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXAddDeviceToDeviceGroupResponse Post(GXAddDeviceToDeviceGroupRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                foreach (ulong device in request.Devices)
                {
                    foreach (ulong group in request.Groups)
                    {
                        GXAmiDeviceGroupDevice it = new GXAmiDeviceGroupDevice();
                        it.DeviceGroupID = group;
                        it.DeviceID = device;
                        it.Added = DateTime.Now.ToUniversalTime();
                        Db.Insert(it);
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, it));
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }
            return new GXAddDeviceToDeviceGroupResponse();
        }

        public GXRemoveDeviceFromDeviceGroupResponse Post(GXRemoveDeviceFromDeviceGroupRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                foreach (ulong device in request.Devices)
                {
                    foreach (ulong group in request.Groups)
                    {
                        string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroupDevice>(Db);
                        query += string.Format("WHERE DeviceID = {0} AND DeviceGroupID = {1}", device, group);
                        List<GXAmiDeviceGroupDevice> items = Db.Select<GXAmiDeviceGroupDevice>(query);
                        foreach (GXAmiDeviceGroupDevice it in items)
                        {
                            Db.DeleteById<GXAmiDeviceGroupDevice>(it);
                            events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, group));
                        }                        
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }
            return new GXRemoveDeviceFromDeviceGroupResponse();
        }

        public GXAddDeviceGroupToUserGroupResponse Post(GXAddDeviceGroupToUserGroupRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                if (request.DeviceGroups == null)
                {
                    throw new ArgumentNullException("DeviceGroups is null.");
                }
                if (request.UserGroups == null)
                {
                    throw new ArgumentNullException("DeviceGroups is null.");
                }

                foreach (ulong dg in request.DeviceGroups)
                {
                    foreach (long ug in request.UserGroups)
                    {
                        GXAmiUserGroupDeviceGroup it = new GXAmiUserGroupDeviceGroup();
                        it.DeviceGroupID = dg;
                        it.UserGroupID = ug;
                        it.Added = DateTime.Now.ToUniversalTime();
                        Db.Insert(it);
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, it));
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }
            return new GXAddDeviceGroupToUserGroupResponse();
        }

        public GXRemoveDeviceGroupFromUserGroupResponse Post(GXRemoveDeviceGroupFromUserGroupRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                foreach (ulong dg in request.DeviceGroups)
                {
                    foreach (ulong ug in request.UserGroups)
                    {
                        string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db);
                        query += string.Format("WHERE DeviceGroupID = {0} AND UserGroupID = {1}", dg, ug);
                        List<GXAmiUserGroupDeviceGroup> items = Db.Select<GXAmiUserGroupDeviceGroup>(query);
                        foreach (GXAmiUserGroupDeviceGroup it in items)
                        {
                            Db.DeleteById<GXAmiUserGroupDeviceGroup>(it.Id);
                            events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, dg));
                        }
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }
            return new GXRemoveDeviceGroupFromUserGroupResponse();
        }


        /// <summary>
        /// Can user access device group.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="DeviceGroupId"></param>
        /// <returns></returns>
        static public bool CanAccess(IDbConnection Db, long userGroupId, ulong DeviceGroupId)
        {
            string query = "SELECT DeviceGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db);
            query += string.Format(" WHERE UserGroupID = {0} AND DeviceGroupID = {1}", userGroupId, DeviceGroupId);
            return Db.Select<GXAmiDeviceGroup>(query).Count != 0;
        }      

        /// <summary>
        /// Can user access device group.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="DeviceGroupId"></param>
        /// <returns></returns>
        static public bool CanUserAccessDeviceGroup(IDbConnection Db, long userId, ulong deviceGroupId)
        {
            string query = "SELECT {0}.ID FROM {0}";
            query += "INNER JOIN {1} ON {0}.UserGroupID = {1}.ID ";
            query += "INNER JOIN {2} ON {2}.UserGroupID = {1}.ID ";
            query += "WHERE {0}.Removed IS NULL AND {1}.Removed IS NULL AND ";
            query += "{2}.Removed IS NULL AND {2}.UserID = {3} AND DeviceGroupID = {4}";
            query = string.Format(query, 
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db), 
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db),
                userId, deviceGroupId);
            return Db.Select<GXAmiDeviceGroup>(query).Count != 0;
        }

        List<GXAmiDeviceGroup> GetDeviceGroups(long userId, long userGroupId, bool removed)
        {
            IAuthSession s = this.GetSession(false);
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db) + " ";
            if (!admin || userGroupId != 0 || userId != 0)
            {
                query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db) + " ON " + 
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db) + ".DeviceGroupID = " + 
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db) + ".ID ";
            }
            if (!removed)
            {
                Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db) + ".Removed IS NULL");
                if (!admin)
                {
                    Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db) + ".Removed IS NULL");
                }
            }
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }
            if (!admin || userGroupId != 0 || userId != 0)
            {
                if (Filter.Count != 0)
                {
                    query += " AND ";
                }
                else
                {
                    query += " WHERE ";
                }
                query += "" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db) + 
                    ".UserGroupID IN (";
                if (userGroupId != 0)
                {
                    query += userGroupId.ToString();
                }
                else
                {
                    if (userId != 0)
                    {
                        query += "SELECT UserGroupID FROM " + 
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + 
                            " WHERE UserID = " + userId;
                    }
                    else
                    {
                        query += "SELECT UserGroupID FROM " + 
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + 
                            " WHERE UserID = " + id;
                    }
                    query += " AND Removed IS NULL";
                }                
                query += ")";
            }
            return Db.Select<GXAmiDeviceGroup>(query);
        }

        /// <summary>
        /// Get available device groups.
        /// </summary>
        public GXDeviceGroupResponse Post(GXDeviceGroupsRequest request)
        {
            List<GXAmiDeviceGroup> list;
            //Return device group to edit.
            if (request.Id != 0)
            {
                list = new List<GXAmiDeviceGroup>();
                list.Add(Db.GetById<GXAmiDeviceGroup>(request.Id));
            }
            //Returns the device group(s) to which the device belongs to.
            else if (request.DeviceId != 0)
            {
                throw new NotImplementedException("DeviceId");
            }
            //Returns the device group(s) to which the user belongs to.
            else if (request.UserId != 0)
            {
                list = GetDeviceGroups(request.UserId, 0, request.Removed);
            }
            //Returns the device group(s) to which the user group belongs to.
            else if (request.UserGroupId != 0)
            {
                list = GetDeviceGroups(0, request.UserGroupId, request.Removed);
            }
            //Return all device groups.
            else
            {
                list = GetDeviceGroups(0, 0, request.Removed);
            }

            //Remove excluded devices.
            if (request.Excluded != null && request.Excluded.Length != 0)
            {
                List<ulong> ids = new List<ulong>(request.Excluded);
                var excludeUserGroups = from c in list where !ids.Contains(c.Id) select c;
                list = excludeUserGroups.ToList();
            }
            //Get devices by range.
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
            return new GXDeviceGroupResponse(list.ToArray());
        }

        /// <summary>
        /// Delete selected device group.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDeviceGroupDeleteResponse Post(GXDeviceGroupDeleteRequest request)
        {
            IAuthSession s = this.GetSession(false);
            //Normal user can't remove device group.
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            
            long id = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            foreach (ulong it in request.DeviceGroupIDs)
            {
                if (it == 0)
                {
                    throw new ArgumentException("ID is required");
                }
                if (!superAdmin && !CanUserAccessDeviceGroup(Db, id, it))
                {
                    throw new ArgumentException("Access denied.");
                }
                GXAmiDeviceGroup dg = Db.QueryById<GXAmiDeviceGroup>(it);
                if (request.Permanently)
                {
                    Db.DeleteById<GXAmiDeviceGroup>(it);
                }
                else
                {
                    dg.Removed = DateTime.Now;
                    Db.UpdateOnly(dg, p => p.Removed, p => p.Id == it);
                }
                events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Remove, dg));                
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXDeviceGroupDeleteResponse();
        }			
	}
}
