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
                        it.Added = DateTime.Now;
                        Db.Insert(it);                        
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Add, it));                        
                        //Add adder to user group if adder is not super admin.
                        foreach (long ugid in request.UserGroupIDs)
                        {
                            //Can user access to the device group.
                            if (!superAdmin && !GXUserGroupService.CanAccess(Db, adderId, ugid))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            GXAmiUserGroupDeviceGroup g = new GXAmiUserGroupDeviceGroup();
                            g.DeviceGroupID = it.Id;
                            g.UserGroupID = ugid;
                            g.Added = DateTime.Now;
                            Db.Insert(g);
                            GXAmiUserGroup ug = Db.GetById<GXAmiUserGroup>(ugid);
                            events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, ug));
                        }
                    }
                    else //Update device group.
                    {
                        Db.Update(it);
                        events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, it));                        
                        if (request.UserGroupIDs != null)
                        {
                            foreach (long ugid in request.UserGroupIDs)
                            {
                                if (!superAdmin && !CanAccess(Db, ugid, it.Id))
                                {
                                    throw new ArgumentException("Access denied.");
                                }
                                if (ugid == 0)
                                {
                                    GXAmiUserGroupDeviceGroup g = new GXAmiUserGroupDeviceGroup();
                                    g.DeviceGroupID = it.Id;
                                    g.UserGroupID = ugid;
                                    g.Added = DateTime.Now;
                                    Db.Insert(g);
                                    it.Id = (ulong)Db.GetLastInsertId();
                                    GXAmiUserGroup ug = Db.GetById<GXAmiUserGroup>(ugid);
                                    events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, ug));
                                }
                            }
                        }                                                
                    }
                    //Bind user groups to the device groups.
                    if (request.UserGroupIDs != null)
                    {
                        foreach (long uid in request.UserGroupIDs)
                        {
                            string query = string.Format("SELECT ID FROM " +
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db) +
                                "WHERE UserGroupID = {0} AND DeviceGroupID = {1}", uid, it.Id);
                            if (Db.Select<GXAmiUserGroupDeviceGroup>(query).Count == 0)
                            {
                                GXAmiUserGroupDeviceGroup item = new GXAmiUserGroupDeviceGroup();
                                item.UserGroupID = uid;
                                item.DeviceGroupID = it.Id;
                                item.Added = DateTime.Now;
                                Db.Insert(it);
                                GXAmiUserGroup ug = Db.GetById<GXAmiUserGroup>(uid);
                                events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, ug));
                                events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, it));
                            }
                        }
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }            
            return new GXDeviceGroupUpdateResponse(request.Items);
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

        /*
         * 
         * /// <summary>
        /// Can user access device group.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="DeviceGroupId"></param>
        /// <returns></returns>
        static public bool CanAccess(IDbConnection Db, long userId, ulong deviceId)
        {
            string query = string.form"SELECT " + GuruxAMI.Server.AppHost.GetTableName<UserGroupDeviceGroup>(Db) + ".ID FROM " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroupDeviceGroup>(Db) + " INNER JOIN " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroup>(Db) + " ON ";
            query += "" + GuruxAMI.Server.AppHost.GetTableName<UserGroupDeviceGroup>(Db) + ".UserGroupID = " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroup>(Db) + ".ID ";
            query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<UserGroupUser>(Db) + " ON " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroupUser>(Db) + ".UserGroupID = " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroup>(Db) + ".ID ";
            query += string.Format("WHERE " + GuruxAMI.Server.AppHost.GetTableName<UserGroup>(Db) + ".Removed IS NULL AND " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroupUser>(Db) + ".Removed IS NULL AND " +
                GuruxAMI.Server.AppHost.GetTableName<UserGroupUser>(Db) + ".UserID = {0} AND DeviceGroupID = {1}",
                userId, deviceGroupId);
            return Db.Select<DeviceGroup>(query).Count != 0;            
        }*/

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
            if (!admin)
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
            if (!admin)
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
            //Returns the device group(s) to which the device belongs to.
            if (request.DeviceId != 0)
            {
                list = null;//TODO: Not implemented.
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
            return new GXDeviceGroupResponse(list.ToArray());
        }

        /// <summary>
        /// Delete selected device group.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDeviceGroupDeleteResponse Delete(GXDeviceGroupDeleteRequest request)
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
                if (request.Permamently)
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
