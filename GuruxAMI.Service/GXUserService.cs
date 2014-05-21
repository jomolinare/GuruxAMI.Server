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
using System.Linq;
using GuruxAMI.Server;
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
    /// Service handles user functionality.
    /// </summary>    
    [Authenticate()]
    internal class GXUserService : GXService
	{
        /// <summary>
        /// Add or update new user.
        /// </summary>		
        public GXUserUpdateResponse Put(GXUserUpdateRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            bool edit = GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s);
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            long adderId = Convert.ToInt64(s.Id);
            lock (Db)
            {
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    //Add new users            
                    foreach (GXAmiUser it in request.Users)
                    {
                        if (string.IsNullOrEmpty(it.Name))
                        {
                            throw new ArgumentException("Invalid name.");
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
                            if (string.IsNullOrEmpty(it.Password))
                            {
                                throw new ArgumentException("Invalid Password.");
                            }
                            it.Added = DateTime.Now.ToUniversalTime();
                            Db.Insert(it);
#if !SS4
                            it.Id = Db.GetLastInsertId();
#else
                            it.Id = Db.LastInsertId();
#endif
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
                            //Get Added time.
#if !SS4
                            GXAmiUser orig = Db.GetById<GXAmiUser>(it.Id);
#else
                            GXAmiUser orig = Db.SingleById<GXAmiUser>(it.Id);
#endif                                                        
                            it.Added = orig.Added;
                            if (string.IsNullOrEmpty(it.Password))
                            {
                                it.Password = orig.Password;
                            }
                            Db.Update(it);
                            events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, it));
                        }
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXUserUpdateResponse(request.Users);
        }

        public static List<GXAmiUser> GetUsers(IAuthSession s, IDbConnection Db, long userId, long groupId, bool removed, bool distinct, string[] search, SearchOperator searchOperator, SearchType searchType)
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

            if (search != null)
            {
                List<string> searching = new List<string>();
                foreach (string it in search)
                {
                    if ((searchType & SearchType.Name) != 0)
                    {
                        string tmp = string.Format("{0}.Name Like('%{1}%')",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db), it);
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
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                List<GXAmiUser> users;
                //Return info from logged user;            
                if (request.UserID == -1)
                {
                    int id = Convert.ToInt32(s.Id);
                    if (id == 0)
                    {
                        throw new ArgumentException("Failed to get information from current user. Invalid session ID.");
                    }
                    users = GetUsers(s, Db, id, 0, false, true, null, SearchOperator.None, SearchType.All);
                }
                else if (request.UserID != 0)
                {
                    users = GetUsers(s, Db, request.UserID, 0, request.Removed, true, null, SearchOperator.None, SearchType.All);
                }
                //Returns users who can access device.
                else if (request.DeviceID != 0)
                {
                    throw new NotImplementedException();
                }
                //Returns users who belong to user group.
                else if (request.UserGroupID != 0)
                {
                    bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                    if (!superAdmin && !GXUserGroupService.CanAccess(Db, Convert.ToInt32(s.Id), request.UserGroupID))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    users = GetUsers(s, Db, 0, request.UserGroupID, request.Removed, true, null, SearchOperator.None, SearchType.All);
                }
                else
                {
                    //Return users who user can see.
                    users = GetUsers(s, Db, 0, 0, request.Removed, true, null, SearchOperator.None, SearchType.All);
                }
                //Remove excluded users.
                if (request.Excluded != null && request.Excluded.Length != 0)
                {
                    List<long> ids = new List<long>(request.Excluded);
                    var excludeUsers = from c in users where !ids.Contains(c.Id) select c;
                    users = excludeUsers.ToList();
                }
                //Get users by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > users.Count)
                    {
                        request.Count = users.Count - request.Index;
                    }
                    users.RemoveRange(0, request.Index);
                    var limitUsers = users.Take(request.Count);
                    users = limitUsers.ToList();
                }
                //Password is not give to the caller. This is a security reason.
                foreach (GXAmiUser it in users)
                {
                    it.Password = null;
                }
                return new GXUsersResponse(users.ToArray());
            }
		}

        /// <summary>
        /// Delete selected user.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXUserDeleteResponse Post(GXUserDeleteRequest request)
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
            lock (Db)
            {
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
#if !SS4
                    GXAmiUser user = Db.QueryById<GXAmiUser>(it);
#else
                    GXAmiUser user = Db.SingleById<GXAmiUser>(it);
#endif                   
                    //Remove user from the user group.
                    if (request.GroupIDs != null && request.GroupIDs.Length != 0)
                    {
                        foreach (long gid in request.GroupIDs)
                        {
                            if (!superAdmin)
                            {
                                List<GXAmiUser> list = GetUsers(s, Db, 0, gid, false, false, null, SearchOperator.None, SearchType.All);
                                if (list.Count == 1)
                                {
                                    throw new ArgumentException("Remove not allowed.");
                                }
                            }
                            string query = string.Format("UserGroupID = {0} AND UserID = {1}", gid, it);
                            GXAmiUserGroupUser item = Db.Select<GXAmiUserGroupUser>(query)[0];
                            Db.Delete<GXAmiUserGroupUser>(item);
#if !SS4
                            GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(gid);
#else
                            GXAmiUserGroup ug = Db.SingleById<GXAmiUserGroup>(gid);
#endif                           
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
                        if (request.Permanently)
                        {
                            Db.DeleteById<GXAmiUser>(it);
                        }
                        else
                        {
                            user.Removed = DateTime.Now.ToUniversalTime();
                            Db.UpdateOnly(user, p => p.Removed, p => p.Id == it);
                        }
                        events.Add(new GXEventsItem(ActionTargets.User, Actions.Remove, user));
                        //Remove all user groups of the user.
                        long[] list = GXUserGroupService.GetUserGroups(Db, it);

                        //TODO: Remove only if last user.
                        foreach (long gid in list)
                        {
#if !SS4
                            GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(gid);
#else
                            GXAmiUserGroup ug = Db.SingleById<GXAmiUserGroup>(gid);
#endif                           
                            if (request.Permanently)
                            {
                                Db.DeleteById<GXAmiUserGroup>(gid);
                            }
                            else
                            {
                                ug.Removed = DateTime.Now.ToUniversalTime();
                                Db.UpdateOnly(ug, p => p.Removed, p => p.Id == gid);
                            }
                            events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, user));
                            events.Add(new GXEventsItem(ActionTargets.User, Actions.Edit, ug));
                        }
                    }
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXUserDeleteResponse();
        }
	}
}
