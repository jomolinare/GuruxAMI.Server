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
    /// Service handles user functionality.
    /// </summary>
    [Authenticate]
    internal class GXUserService : ServiceStack.ServiceInterface.Service
	{
        /// <summary>
        /// Add or update new user.
        /// </summary>
		public GXUserUpdateResponse Put(GXUserUpdateRequest request)
		{
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                List<GXEventsItem> events = new List<GXEventsItem>();
                IAuthSession s = this.GetSession(false);
                bool edit = GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s);
                bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                long adderId = Convert.ToInt64(s.Id);
                //Add new users            
                foreach (GXAmiUser it in request.Users)
                {
                    if (string.IsNullOrEmpty(it.Name))
                    {
                        throw new ArgumentException("Invalid name.");
                    }
                    if (string.IsNullOrEmpty(it.Password))
                    {
                        throw new ArgumentException("Invalid Password.");
                    }
                    //If new user
                    if (it.Id == 0)
                    {
                        //User can't add new users.
                        if (!edit)
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        if (!superAdmin && (it.AccessRights & UserAccessRights.SuperAdmin) == UserAccessRights.SuperAdmin)
                        {
                            throw new ArgumentException("Only super admin can add new super admin.");
                        }
                        it.Added = DateTime.Now;
                        Db.Insert(it);
                        it.Id = Db.GetLastInsertId();
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Add, it));
                    }
                    else //Update user data.
                    {
                        //User can only edit itself.
                        if (!edit && adderId != it.Id)
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        if (!superAdmin)
                        {
                            //User can't update user data if he do not have access to the user group.
                            long[] groups1 = GXUserGroupService.GetUserGroups(Db, adderId);
                            long[] groups2 = GXUserGroupService.GetUserGroups(Db, it.Id);
                            bool found = false;
                            foreach (long it1 in groups1)
                            {
                                foreach (long it2 in groups2)
                                {
                                    if (it1 == it2)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (found)
                                {
                                    break;
                                }
                            }
                            if (!found)
                            {
                                throw new ArgumentException("Access denied.");
                            }
                        }
                        Db.Update(it);
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, it));                        
                    }
                    if (request.UserGroups != null)
                    {
                        //Add user to user group.
                        foreach (GXAmiUserGroup ug in request.UserGroups)
                        {
                            if (ug.Id == 0)
                            {
                                Db.Insert(ug);
                                ug.Added = DateTime.Now;
                                ug.Id = Db.GetLastInsertId();
                            }
                            //Check that user have access to the group.
                            else if (!superAdmin &&
                                !GXUserGroupService.CanAccess(Db, adderId, ug.Id))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            GXAmiUserGroupUser g;
                            string query = string.Format("SELECT UserID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + "WHERE UserID = {0} AND UserGroupID = {1}", it.Id, ug.Id);
                            if (Db.Select<GXAmiUser>(query).Count == 0)
                            {
                                g = new GXAmiUserGroupUser();
                                g.UserID = it.Id;
                                g.UserGroupID = ug.Id;
                                g.Added = DateTime.Now;
                                Db.Insert(g);
                                events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, it));
                                events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, ug));
                            }

                            //Add adder to user group if adder is not super admin.
                            if (!superAdmin)
                            {
                                query = string.Format("SELECT UserID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE UserID = {0} AND UserGroupID = {1}", adderId, ug.Id);
                                if (Db.Select<GXAmiUser>(query).Count == 0)
                                {
                                    g = new GXAmiUserGroupUser();
                                    g.UserID = adderId;
                                    g.UserGroupID = ug.Id;
                                    g.Added = DateTime.Now;
                                    Db.Insert(g);
                                }
                            }
                        }
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();                
                return new GXUserUpdateResponse(request.Users, request.UserGroups);
            }

		}

        public static List<GXAmiUser> GetUsers(IAuthSession s, IDbConnection Db, long userId, long groupId, bool removed, bool distinct)
        {            
            string id = s.Id;
            if (Convert.ToInt32(id) == 0)
            {
                throw new ArgumentException("Invalid session ID.");
            }
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            string query;
            if (distinct)
            {
                query = "SELECT DISTINCT " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + ".* FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + " ";
            }
            else
            {
                query = "SELECT " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + ".* FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + " ";
            }
            
            List<string> Filter = new List<string>();
            if (!admin || groupId != 0)
            {
                query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " ON " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserID = " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + ".ID ";
            }
            if (!removed)
            {
                Filter.Add("" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + ".Removed IS NULL");
                if (!admin)
                {
                    Filter.Add("" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".Removed IS NULL ");
                }
            }
            if (userId != 0)
            {
                Filter.Add("" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db) + ".ID = " + userId.ToString());
            }
            if (groupId != 0)
            {
                Filter.Add("UserGroupID = " + groupId.ToString());
            }
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }
            if (!admin && groupId == 0)
            {
                if (Filter.Count == 0)
                {
                    query += "WHERE ";
                }
                else
                {
                    query += " AND ";
                }
                query += "" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserID IN (";
                query += "SELECT UserID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserGroupID IN";
                query += "(";
                query += "SELECT UserGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE UserID = " + id;
                if (!removed)
                {
                    query += " AND Removed IS NULL";
                }
                query += "))";
            }
            query += string.Format(" ORDER BY {0}.ID", GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db));
            return Db.Select<GXAmiUser>(query);
        }

        /// <summary>
        /// Get available users.
        /// </summary>
		public GXUsersResponse Post(GXUsersRequest request)
		{
            IAuthSession s = this.GetSession(false);
            List<GXAmiUser> users;
            //Returns users who can access device.
            if (request.DeviceID != 0)
            {
                users = null;
            }
            //Returns users who belong to user group.
            else if (request.UserGroupID != 0)
            {                
                bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                if (!superAdmin && !GXUserGroupService.CanAccess(Db, Convert.ToInt32(s.Id), request.UserGroupID))
                {
                    throw new ArgumentException("Access denied.");
                }
                users = GetUsers(s, Db, 0, request.UserGroupID, request.Removed, true);
            }
            else
            {
                //Return users who user can see.
                users = GetUsers(s, Db, 0, 0, request.Removed, true);
            }
            return new GXUsersResponse(users.ToArray());
		}

        /// <summary>
        /// Delete selected user.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXUserDeleteResponse Delete(GXUserDeleteRequest request)
		{
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            if (id == 0)
            {
                throw new ArgumentException("Remove failed. Invalid session ID.");
            }
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Remove not allowed.");
            }
            List<GXEventsItem> events = new List<GXEventsItem>();
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            foreach (int it in request.UserIDs)
            {
                if (it == 0)
                {
                    throw new ArgumentException("ID is required");
                }
                if (!superAdmin && !GXUserGroupService.CanAccess(Db, id, it))
                {
                    throw new ArgumentException("Access denied.");
                }
                GXAmiUser user = Db.QueryById<GXAmiUser>(it);
                //Remove user from the user group.
                if (request.GroupIDs != null && request.GroupIDs.Length != 0)
                {
                    foreach (long gid in request.GroupIDs)
                    {
                        if (!superAdmin)
                        {
                            List<GXAmiUser> list = GetUsers(s, Db, 0, gid, false, false);
                            if (list.Count == 1)
                            {
                                throw new ArgumentException("Remove not allowed.");
                            }
                        }
                        string query = string.Format("UserGroupID = {0} AND UserID = {1}", gid, it);
                        GXAmiUserGroupUser item = Db.Select<GXAmiUserGroupUser>(query)[0];
                        if (request.Permamently)
                        {
                            Db.Delete<GXAmiUserGroupUser>(item);
                        }
                        else
                        {
                            item.Removed = DateTime.Now;
                            Db.Update(item);
                        }
                        GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(gid);
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, user));
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, ug));
                    }
                }
                else //Remove user.
                {
                    // You can not delete yourself.
                    if (it == id)
                    {
                        throw new ArgumentException("Remove not allowed.");
                    }                    
                    if (request.Permamently)
                    {
                        Db.DeleteById<GXAmiUser>(it);
                    }
                    else
                    {
                        user.Removed = DateTime.Now;
                        Db.UpdateOnly(user, p => p.Removed, p => p.Id == it);
                    }
                    events.Add(new GXEventsItem(ActionTargets.User, Actions.Remove, user));                    
                    //Remove all user groups of the user.
                    long[] list = GXUserGroupService.GetUserGroups(Db, it);
                    
                    //TODO: Remove only if last user.
                    foreach (long gid in list)
                    {
                        GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(gid);
                        if (request.Permamently)
                        {
                            Db.DeleteById<GXAmiUserGroup>(gid);
                        }
                        else
                        {
                            ug.Removed = DateTime.Now;
                            Db.UpdateOnly(ug, p => p.Removed, p => p.Id == gid);
                        }
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, user));
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, ug));
                    }
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
			return new GXUserDeleteResponse();
		}
	}
}
