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
using ServiceStack.ServiceHost;
using ServiceStack.ServiceInterface;
using ServiceStack.WebHost.Endpoints;

namespace GuruxAMI.Server
{
    public class GXServiceRunner<TRequest> : ServiceRunner<TRequest>
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="appHost"></param>
        /// <param name="actionContext"></param>
        public GXServiceRunner(IAppHost appHost, ActionContext actionContext)
            : base(appHost, actionContext)
        {

        }

        public override void OnBeforeExecute(IRequestContext requestContext, TRequest request)
        {
            base.OnBeforeExecute(requestContext, request);
        }

        static List<ulong> GetExecutedAction(TRequest request, out ActionTargets target, out Actions action)
        {
            List<ulong> list = new List<ulong>();
            if (request is GXCreateTablesRequest)
            {
                target = ActionTargets.Database;
                action = Actions.Add;
            }
            else if (request is GXDropTablesRequest)
            {
                target = ActionTargets.Database;
                action = Actions.Remove;
            }
            else if (request is GXUserUpdateRequest)
            {
                target = ActionTargets.User;
                action = (request as GXUserUpdateRequest).Action;
                foreach(GXAmiUser user in (request as GXUserUpdateRequest).Users)
                {
                    list.Add((ulong) user.Id);
                }
            }
            else if (request is GXUserDeleteRequest)
            {
                target = ActionTargets.User;
                action = Actions.Remove;
                foreach (GXAmiUser user in (request as GXUserUpdateRequest).Users)
                {
                    list.Add((ulong)user.Id);
                }
            }
            else if (request is GXUsersRequest)
            {
                target = ActionTargets.User;
                action = Actions.Get;
            }
            else if (request is GXUserGroupUpdateRequest)
            {
                target = ActionTargets.UserGroup;
                action = (request as GXUserGroupUpdateRequest).Action;
            }
            else if (request is GXUserGroupDeleteRequest)
            {
                target = ActionTargets.UserGroup;
                action = Actions.Remove;
            }
            else if (request is GXUserGroupsRequest)
            {
                target = ActionTargets.UserGroup;
                action = Actions.Get;
            }
            else if (request is GXDeviceUpdateRequest)
            {
                target = ActionTargets.Device;
                action = (request as GXDeviceUpdateRequest).Action;
            }
            else if (request is GXDeviceDeleteRequest)
            {
                target = ActionTargets.Device;
                action = Actions.Remove;
            }
            else if (request is GXDevicesRequest)
            {
                target = ActionTargets.Device;
                action = Actions.Get;
            }
            else if (request is GXDeviceGroupUpdateRequest)
            {
                target = ActionTargets.DeviceGroup;
                action = (request as GXDeviceGroupUpdateRequest).Action;
            }
            else if (request is GXDeviceGroupDeleteRequest)
            {
                target = ActionTargets.DeviceGroup;
                action = Actions.Remove;
            }
            else if (request is GXDeviceGroupsRequest)
            {
                target = ActionTargets.DeviceGroup;
                action = Actions.Get;
            }
            else if (request is GXTaskUpdateRequest)
            {
                target = ActionTargets.Task;
                action = Actions.Add;
            }
            else if (request is GXTaskDeleteRequest)
            {
                target = ActionTargets.Task;
                action = Actions.Remove;
            }
            else if (request is GXTasksRequest)
            {
                target = ActionTargets.Task;
                action = Actions.Get;
            }
            else if (request is GXDeviceTemplateUpdateRequest)
            {
                target = ActionTargets.DeviceTemplate;
                action = Actions.Edit;
            }
            else if (request is GXDeviceTemplateDeleteRequest)
            {
                target = ActionTargets.DeviceTemplate;
                action = Actions.Remove;
            }
            else if (request is GXDeviceTemplatesRequest)
            {
                target = ActionTargets.DeviceTemplate;
                action = Actions.Get;
            }
            else if (request is GXDataCollectorUpdateRequest)
            {
                target = ActionTargets.DataCollector;
                action = Actions.Edit;
            }
            else if (request is GXDataCollectorDeleteRequest)
            {
                target = ActionTargets.DataCollector;
                action = Actions.Remove;
            }
            else if (request is GXDataCollectorsRequest)
            {
                target = ActionTargets.DataCollector;
                action = Actions.Get;
            }
            else if (request is GXEventsRequest)
            {
                target = ActionTargets.None;
                action = Actions.None;
            }
            else
            {
                throw new Exception("Invalid target.");
            }
            return list;
        }       

        /// <summary>
        /// Save exceptions.
        /// </summary>
        /// <param name="requestContext"></param>
        /// <param name="request"></param>
        /// <param name="ex"></param>
        /// <returns></returns>
        public override object HandleException(IRequestContext requestContext,
            TRequest request, Exception ex)
        {
            //Do not handle connection close exceptions.
            if (ex is System.Net.WebException && (ex as System.Net.WebException).Status != System.Net.WebExceptionStatus.ConnectionClosed)
            {
                try
                {
                    var httpReq = requestContext.Get<ServiceStack.ServiceHost.IHttpRequest>();
                    int id = 0;
                    if (int.TryParse(httpReq.GetSession(false).Id, out id) && id != 0)
                    {
                        ActionTargets target;
                        Actions action;
                        GetExecutedAction(request, out target, out action);
                        IDbConnectionFactory f = this.TryResolve<IDbConnectionFactory>();
                        using (IDbConnection Db = f.OpenDbConnection())
                        {
                            GXAmiSystemError e = new GXAmiSystemError(id, target, action, ex);
                            Db.Insert(e);
                        }
                    }
                    else //If DC. TODO:
                    {
                    }
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine(ex2.Message);
                }
            }
            return base.HandleException(requestContext, request, ex);
        }        
    }
}
