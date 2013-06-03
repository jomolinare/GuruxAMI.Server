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
    /// Handles user group add, remove and update services.
    /// </summary>
    [Authenticate]
    internal class GXUserGroupService : ServiceStack.ServiceInterface.Service
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
                        it.Added = DateTime.Now;
                        Db.Insert(it);
                        it.Id = Db.GetLastInsertId();
                        //Add adder to user group if adder is not super admin.
                        if (!superAdmin)
                        {
                            GXAmiUserGroupUser g = new GXAmiUserGroupUser();
                            g.UserID = Convert.ToInt64(s.Id);
                            g.UserGroupID = it.Id;
                            g.Added = DateTime.Now;
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
                        Db.Update(it);
                        events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Edit, it));
                    }
                    foreach (GXAmiUser u in it.Users)
                    {
                        //GXUserService
                    }
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
                trans.Commit();
            }            
            return new GXUserGroupUpdateResponse(request.UserGroups);
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
                query += "SELECT UserGroupID FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db) + " WHERE UserID = " + id;
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
            List<GXAmiUserGroup> list;
            //Returns the user group(s) to which the device belongs to.
            if (request.DeviceId != 0)
            {
                list = null; //TODO: Not implemented.
            }
            //Returns the user group(s) to which the device group belongs to.
            else if (request.DeviceGroupId != 0)
            {
                list = null;
            }
            else
            {
                list = GetUserGroups(request.UserId, request.Removed);
            }
            return new GXUserGroupResponse(list.ToArray());
		}

        /// <summary>
        /// Delete selected user group.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXUserGroupDeleteResponse Delete(GXUserGroupDeleteRequest request)
		{            
            IAuthSession s = this.GetSession(false);
            //Normal user can't remove device.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long id = Convert.ToInt64(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
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
                GXAmiUserGroup ug = Db.QueryById<GXAmiUserGroup>(it);
                if (request.Permamently)
                {
                    Db.DeleteById<GXAmiUserGroup>(it);
                }
                else
                {
                    ug.Removed = DateTime.Now;
                    Db.UpdateOnly(ug, p => p.Removed, p => p.Id == it);
                }
                events.Add(new GXEventsItem(ActionTargets.UserGroup, Actions.Remove, ug));       
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
			return new GXUserGroupDeleteResponse();
		}
	}
}
