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
using System.Linq;
using ServiceStack.ServiceInterface.Auth;
using GuruxAMI.Server;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles error functionality.
    /// </summary>
	[Authenticate]
    internal class GXErrorService : ServiceStack.ServiceInterface.Service
	{
		public GXErrorUpdateResponse Put(GXErrorUpdateRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            GXAmiDeviceError err = new GXAmiDeviceError();
            err.TaskID = request.TaskID;
            err.TargetDeviceID = request.DeviceID;
            err.TimeStamp = DateTime.Now;
            err.Message = request.Message;
            err.Source = request.Source;
            int len = request.StackTrace.Length;
            if (len > 255)
            {
                len = 255;
            }
            err.StackTrace = request.StackTrace.Substring(0, len);
            err.Severity = request.Severity;
            events.Add(new GXEventsItem(ActionTargets.DeviceError, Actions.Add, err));
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                Db.Insert(err);
                trans.Commit();
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXErrorUpdateResponse();
		}
		public GXErrorsResponse Post(GXErrorsRequest request)
		{            
            if (request.System)
            {
                List<string> Filter = new List<string>();
                string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiSystemError>(Db);
                if (request.UserIDs != null && request.UserIDs.Length != 0)
                {
                    bool first = true;
                    query += "WHERE UserID IN (";
                    foreach (long it in request.UserIDs)
                    {
                        if (!first)
                        {
                            query += ", ";
                        }
                        query += it.ToString();
                        first = false;
                    }
                    query += ")";
                }
                else if (request.UserGroupIDs != null && request.UserGroupIDs.Length != 0)
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
                    first = true;
                    List<long> userIDs = Db.Select<long>(query2);
                    first = true;
                    query += "WHERE UserID IN (";
                    foreach (long it in userIDs)
                    {
                        if (!first)
                        {
                            query += ", ";
                        }
                        query += it.ToString();
                        first = false;
                    }
                    query += ")";
                }
                List<GXAmiSystemError> errors = Db.Select<GXAmiSystemError>(query);
                //Get errors by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > errors.Count)
                    {
                        request.Count = errors.Count - request.Index;
                    }
                    errors.RemoveRange(0, request.Index);
                    var limitUsers = errors.Take(request.Count);
                    errors = limitUsers.ToList();
                }
                return new GXErrorsResponse(errors.ToArray());
            }
            else
            {

                List<string> Filter = new List<string>();
                string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceError>(Db);
                if (request.DeviceIDs != null && request.DeviceIDs.Length != 0)
                {
                    bool first = true;
                    string str = "TargetDeviceID IN (";
                    foreach (long it in request.DeviceIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    str += ")";
                    Filter.Add(str);
                }
                if (request.DeviceGroupIDs != null && request.DeviceGroupIDs.Length != 0)
                {
                    //query += "DeviceGroup = " + request.DeviceGroupIDs;
                    bool first = true;
                    string str = "TargetDeviceID IN (";
                    foreach (long it in request.DeviceIDs)
                    {
                        if (!first)
                        {
                            str += ", ";
                        }
                        str += it.ToString();
                        first = false;
                    }
                    str += ")";
                    Filter.Add(str);
                }
                if (Filter.Count != 0)
                {
                    query += "WHERE ";
                    query += string.Join(" AND ", Filter.ToArray());
                }
                List<GXAmiDeviceError> errors = Db.Select<GXAmiDeviceError>(query);
                //Get errors by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > errors.Count)
                    {
                        request.Count = errors.Count - request.Index;
                    }
                    errors.RemoveRange(0, request.Index);
                    var limitUsers = errors.Take(request.Count);
                    errors = limitUsers.ToList();
                }                
                return new GXErrorsResponse(errors.ToArray());
            }
		}
		public GXErrorDeleteResponse Post(GXErrorDeleteRequest request)
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
            List<GXAmiDeviceError> errors = new List<GXAmiDeviceError>();
            List<GXAmiSystemError> sysErrors = new List<GXAmiSystemError>();
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            //Remove system errors.
            if (request.System || request.SystemErrorIDs != null)
            {
                foreach (uint it in request.SystemErrorIDs)
                {
                    if (it == 0)
                    {
                        throw new ArgumentException("ID is required");
                    }
                    sysErrors.AddRange(Db.Select<GXAmiSystemError>(p => p.Id == it));
                }
                foreach (GXAmiSystemError it in sysErrors)
                {
                    events.Add(new GXEventsItem(ActionTargets.SystemError, Actions.Remove, it));
                }
                Db.DeleteAll<GXAmiSystemError>(sysErrors);
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, id, events);
            }
            else
            {
                //Remove device errors by ID.
                if (request.DeviceErrorIDs != null)
                {
                    foreach (uint it in request.DeviceErrorIDs)
                    {
                        if (it == 0)
                        {
                            throw new ArgumentException("ID is required");
                        }
                        errors.AddRange(Db.Select<GXAmiDeviceError>(p => p.Id == it));
                    }
                }
                //Remove device errors.
                if (request.DeviceID != 0)
                {
                    errors.AddRange(Db.Select<GXAmiDeviceError>(p => p.TargetDeviceID == request.DeviceID));
                }
                if (errors.Count == 0)
                {
                    //Remove all log items.
                    if (superAdmin)
                    {
                        errors.AddRange(Db.Select<GXAmiDeviceError>());
                    }
                }
                foreach (GXAmiDeviceError it in errors)
                {
                    events.Add(new GXEventsItem(ActionTargets.DeviceError, Actions.Remove, it));
                }
                Db.DeleteAll<GXAmiDeviceError>(errors);
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, id, events);
            }                        
			return new GXErrorDeleteResponse();
		}
	}
}
