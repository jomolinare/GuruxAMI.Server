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
    /// Service handles action functionality.
    /// </summary>
	[Authenticate]
#if !SS4
    internal class GXActionService : ServiceStack.ServiceInterface.Service
#else
    internal class GXActionService : ServiceStack.Service
#endif    
	{
		public GXActionResponse Post(GXActionRequest request)
		{
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                List<string> Filter = new List<string>();
                string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiUserActionLog>(Db);
                if (request.DeviceIDs != null && request.DeviceIDs.Length != 0)
                {
                    string str = " TargetID IN(";
                    bool first = true;
                    foreach (ulong it in request.DeviceIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    Filter.Add(str + ")");
                }
                if (request.DeviceGroupIDs != null && request.DeviceGroupIDs.Length != 0)
                {
                    string str = " TargetID IN(";
                    bool first = true;
                    foreach (ulong it in request.DeviceGroupIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    Filter.Add(str + ")");
                }
                if (request.UserIDs != null && request.UserIDs.Length != 0)
                {
                    string str = " UserID IN(";
                    bool first = true;
                    foreach (ulong it in request.UserIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    Filter.Add(str + ")");
                }
                if (request.UserGroupIDs != null && request.UserGroupIDs.Length != 0)
                {
                    string query2 = string.Format("SELECT DISTINCT {0}.ID FROM {0} INNER JOIN {1} ON {0}.ID = {1}.UserID WHERE UserGroupID IN (",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUser>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));
                    bool first = true;
                    foreach (ulong it in request.UserGroupIDs)
                    {
                        if (!first)
                        {
                            query2 += ", ";
                        }
                        query2 += it.ToString();
                        first = false;
                    }
                    string str = " UserID IN(";
                    first = true;
                    List<long> userIDs = Db.Select<long>(query2);
                    foreach (long it in userIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    Filter.Add(str + ")");

                }
                if (Filter.Count != 0)
                {
                    query += "WHERE ";
                    query += string.Join(" AND ", Filter.ToArray());
                }
                if (request.Descending)
                {
                    query += string.Format(" ORDER BY {0}.ID DESC ",                
                             GuruxAMI.Server.AppHost.GetTableName<GXAmiUserActionLog>(Db));
                }
                List<GXAmiUserActionLog> actions = Db.Select<GXAmiUserActionLog>(query);
                //Get actions by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > actions.Count)
                    {
                        request.Count = actions.Count - request.Index;
                    }
                    actions.RemoveRange(0, request.Index);
                    var limitUsers = actions.Take(request.Count);
                    actions = limitUsers.ToList();
                }
                return new GXActionResponse(actions.ToArray());
            }
		}

        public GXActionDeleteResponse Post(GXActionDeleteRequest request)
        {
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                if (id == 0)
                {
                    throw new ArgumentException("Remove failed. Invalid session ID.");
                }
                if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
                {
                    throw new ArgumentException("Remove not allowed.");
                }
                List<GXAmiUserActionLog> logs = new List<GXAmiUserActionLog>();
                bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                //Remove user group logs.
                if (request.UserGroupIDs != null)
                {
                    foreach (long it in request.UserGroupIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("ID is required");
                        }
                        if (!superAdmin && !GXUserGroupService.CanAccess(Db, id, it))
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        logs.AddRange(Db.Select<GXAmiUserActionLog>(p => p.UserID == it));
                    }
                }
                //Remove user logs.
                if (request.UserIDs != null)
                {
                    foreach (long it in request.UserIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("ID is required");
                        }
                        if (!superAdmin || id != it)
                        {
                            throw new ArgumentException("Access denied.");
                        }
                        logs.AddRange(Db.Select<GXAmiUserActionLog>(p => p.UserID == it));
                    }
                }
                //Remove Device logs.
                if (request.DeviceIDs != null)
                {
                    foreach (ulong it in request.DeviceIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("ID is required");
                        }
                        logs.AddRange(Db.Select<GXAmiUserActionLog>(p => p.TargetID == it));
                    }
                }
                //Remove Device group logs.
                if (request.DeviceGroupIDs != null)
                {
                    foreach (ulong it in request.DeviceGroupIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("ID is required");
                        }
                        logs.AddRange(Db.Select<GXAmiUserActionLog>(p => p.TargetID == it));
                    }
                }
                if (logs.Count == 0)
                {
                    //Remove all log items.
                    if (superAdmin)
                    {
                        logs.AddRange(Db.Select<GXAmiUserActionLog>());
                    }
                    else //Remove all log items made by user.
                    {
                        logs.AddRange(Db.Select<GXAmiUserActionLog>(p => p.UserID == id));
                    }
                }
                foreach (GXAmiUserActionLog it in logs)
                {
                    events.Add(new GXEventsItem(ActionTargets.DeviceError, Actions.Remove, it));
                }
                Db.DeleteByIds<GXAmiUserActionLog>(logs);
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            return new GXActionDeleteResponse();
        }
	}
}
