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
    /// Handles user group add, remove and update services.
    /// </summary>
    [Authenticate]
    internal class GXUserGroupService : GXService
	{
        /// <summary>
        /// Add or update new user group.
        /// </summary>
		public GXUserGroupUpdateResponse Put(GXUserGroupUpdateRequest request)
		{
            IAuthSession s = this.GetSession(false);
            //Normal user can't change user group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                    //Add new user groups           
                    foreach (GXAmiUserGroup it in request.UserGroups)
                    {
                        if (string.IsNullOrEmpty(it.Name))
                        {
                            throw new ArgumentException("Invalid name.");
                        }
                        //If new user group.
                        if (it.Id == 0)
                        {
                            it.Added = DateTime.Now.ToUniversalTime();
                            Db.Insert(it);
#if !SS4
                            it.Id = Db.GetLastInsertId();
#else
                            it.Id = Db.LastInsertId();
#endif                            
                            //Add adder to user group if adder is not super admin.
                            if (!superAdmin)
                            {
                                GXAmiUserGroupUser g = new GXAmiUserGroupUser();
                                g.UserID = Convert.ToInt64(s.Id);
                                g.UserGroupID = it.Id;
                                g.Added = DateTime.Now.ToUniversalTime();
                                Db.Insert(g);
                            }
                            events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Add, it));
                        }
                        else //Update user group.
                        {
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
                            GXAmiUserGroup orig = Db.GetById<GXAmiUserGroup>(it.Id);
#else
                            GXAmiUserGroup orig = Db.SingleById<GXAmiUserGroup>(it.Id);                            
#endif                            
                            it.Added = orig.Added.ToUniversalTime();
                            Db.Update(it);
                            events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, it));
                        }
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXUserGroupUpdateResponse(request.UserGroups);
        }

        /// <summary>
        /// Add users to user groups.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXAddUserToUserGroupResponse Post(GXAddUserToUserGroupRequest request)
        {
            IAuthSession s = this.GetSession(false);
            //Normal user can't change user group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                    foreach (long user in request.Users)
                    {
                        foreach (long group in request.Groups)
                        {
                            if (!superAdmin)
                            {
                                //User can't update user data if he do not have access to the user group.
                                long[] groups1 = GXUserGroupService.GetUserGroups(Db, adderId);
                                long[] groups2 = GXUserGroupService.GetUserGroups(Db, group);
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
                            GXAmiUserGroupUser it = new GXAmiUserGroupUser();
                            it.UserGroupID = group;
                            it.UserID = user;
                            it.Added = DateTime.Now.ToUniversalTime();
                            Db.Insert(it);
                            events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, it));
                        }
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXAddUserToUserGroupResponse();
        }

        public GXRemoveUserFromUserGroupResponse Post(GXRemoveUserFromUserGroupRequest request)
        {
            IAuthSession s = this.GetSession(false);
            //Normal user can't change user group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                    foreach (long user in request.Users)
                    {
                        foreach (long group in request.Groups)
                        {
                            if (!superAdmin)
                            {
                                //User can't update user data if he do not have access to the user group.
                                long[] groups1 = GXUserGroupService.GetUserGroups(Db, adderId);
                                long[] groups2 = GXUserGroupService.GetUserGroups(Db, group);
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
                            string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db);
                            query += string.Format("WHERE UserID = {0} AND UserGroupID = {1}", user, group);
                            List<GXAmiUserGroupUser> items = Db.Select<GXAmiUserGroupUser>(query);
                            foreach (GXAmiUserGroupUser it in items)
                            {
                                Db.DeleteById<GXAmiUserGroupUser>(it.Id);
                                events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, group));
                            }
                        }
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXRemoveUserFromUserGroupResponse();
        }

        /// <summary>
        /// Can user access uuser group.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="userGroupId"></param>
        /// <returns></returns>
        static public bool CanAccess(IDbConnection Db, long userId, long userGroupId)
        {
            string query = "SELECT UserGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db);
            query += string.Format("WHERE UserID = {0} AND UserGroupID = {1}", userId, userGroupId);
            return Db.Select<GXAmiUserGroup>(query).Count == 1;
        }

        /// <summary>
        /// Get user group IDs that user can access.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="userGroupId"></param>
        /// <returns></returns>
        static public long[] GetUserGroups(IDbConnection Db, long userId)
        {
            string query = "SELECT " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) +".* FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db);
            query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) +  " ON " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserGroupID = " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".ID ";
            query += string.Format("WHERE " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".Removed IS NULL AND " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".Removed IS NULL AND UserID = {0}", userId);
            var rows = Db.Select<GXAmiUserGroup>(query);
            return rows.ConvertAll(x => x.Id).ToArray();
        }

        /// <summary>
        /// Search user groups that user can access.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="userGroupId"></param>
        /// <returns></returns>
        static public List<GXAmiUserGroup> GetUserGroups(IDbConnection Db, long userId, string[] search, SearchOperator searchOperator, SearchType searchType)
        {
            string query = "SELECT " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".* FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db);
            query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " ON " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserGroupID = " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".ID ";
            query += string.Format("WHERE " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".Removed IS NULL AND " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".Removed IS NULL AND UserID = {0}", userId);

            if (search != null)
            {
                List<string> searching = new List<string>();
                List<string> Filter = new List<string>();
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
                query += string.Join(" AND ", Filter.ToArray());
            }

            List<GXAmiUserGroup> rows = Db.Select<GXAmiUserGroup>(query);
            return rows;
        }

        List<GXAmiUserGroup> GetUserGroups(long userId, bool removed)
        {
            IAuthSession s = this.GetSession(false);
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = "SELECT DISTINCT " + GuruxAMI.Server.AppHost.GetTableName <GXAmiUserGroup>(Db) + ".* FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db);
            if (!admin)
            {
                query += "INNER JOIN " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " ON " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".UserGroupID = " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".ID ";
            }
            if (!removed)
            {
                Filter.Add("" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".Removed IS NULL");
                if (!admin)
                {
                    Filter.Add("" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + ".Removed IS NULL");
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
                query += "" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".ID IN (";
                query += "SELECT DISTINCT UserGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE UserID = " + id;
                if (userId != 0)
                {
                    query += " AND UserID = " + userId.ToString();
                }
                if (!removed)
                {
                    query += " AND Removed IS NULL";
                }
                query += ")";
            }
            else if (userId != 0)
            {
                if (Filter.Count != 0)
                {
                    query += " AND ";
                }
                else
                {
                    query += " WHERE ";
                }
                query += "" + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db) + ".ID IN (";
                query += "SELECT DISTINCT UserGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE UserID = " + userId;
                if (!removed)
                {
                    query += " AND Removed IS NULL";
                }
                query += ")";
            }
            return Db.Select<GXAmiUserGroup>(query);
        }

        /// <summary>
        /// Get available user groups.
        /// </summary>
		public GXUserGroupResponse Post(GXUserGroupsRequest request)
		{
            lock (Db)
            {
                List<GXAmiUserGroup> list;
                if (request.UserGroupId != 0)
                {
                    list = new List<GXAmiUserGroup>();
#if !SS4
                    list.Add(Db.GetById<GXAmiUserGroup>(request.UserGroupId));
#else
                    list.Add(Db.SingleById<GXAmiUserGroup>(request.UserGroupId));                    
#endif                    
                }
                //Returns the user group(s) to which the device belongs to.
                else if (request.DeviceId != 0)
                {
                    throw new NotImplementedException();
                }
                //Returns the user group(s) to which the device group belongs to.
                else if (request.DeviceGroupId != 0)
                {
                    string query = string.Format("SELECT DISTINCT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.UserGroupID WHERE DeviceGroupID={2}",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                        request.DeviceGroupId);
                    list = Db.Select<GXAmiUserGroup>(query);
                }
                else
                {
                    list = GetUserGroups(request.UserId, request.Removed);
                }
                //Remove excluded user groups.
                if (request.Excluded != null && request.Excluded.Length != 0)
                {
                    List<long> ids = new List<long>(request.Excluded);
                    var excludeUserGroups = from c in list where !ids.Contains(c.Id) select c;
                    list = excludeUserGroups.ToList();
                }
                //Get user groups by range.
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
                return new GXUserGroupResponse(list.ToArray());
            }
		}

        /// <summary>
        /// Delete selected user group.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXUserGroupDeleteResponse Post(GXUserGroupDeleteRequest request)
        {
            IAuthSession s = this.GetSession(false);
            //Normal user can't remove device.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long id = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                foreach (int it in request.GroupIDs)
                {
                    if (it == 0)
                    {
                        throw new ArgumentException("ID is required");
                    }
                    if (GetUserGroups(Db, it).Length == 1)
                    {
                        throw new ArgumentException("User must belong atleast one user group.");
                    }
#if !SS4
                    GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(it);
#else
                    GXAmiUserGroup ug = Db.SingleById<GXAmiUserGroup>(it);
#endif                                                                                              
                    if (request.Permanently)
                    {
                        Db.DeleteById<GXAmiUserGroup>(it);
                    }
                    else
                    {
                        ug.Removed = DateTime.Now.ToUniversalTime();
                        Db.UpdateOnly(ug, p => p.Removed, p => p.Id == it);
                    }
                    events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Remove, ug));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXUserGroupDeleteResponse();
        }
	}
}
