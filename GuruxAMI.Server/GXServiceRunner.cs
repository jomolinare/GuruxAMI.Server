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
#if !SS4
using ServiceStack.ServiceHost;
using ServiceStack.ServiceInterface;
using ServiceStack.WebHost.Endpoints;
using System.Net.Mail;
#else
using ServiceStack;
using ServiceStack.Host;
using ServiceStack.Web;
using ServiceStack.Data;
#endif

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
#if !SS4
        public override void OnBeforeExecute(IRequestContext requestContext, TRequest request)
#else
        public virtual void OnBeforeExecute(IRequest requestContext, TRequest request)
#endif
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
            else if (request is GXDeviceProfilesUpdateRequest)
            {
                target = ActionTargets.DeviceProfile;
                action = Actions.Edit;
            }
            else if (request is GXDeviceProfilesDeleteRequest)
            {
                target = ActionTargets.DeviceProfile;
                action = Actions.Remove;
            }
            else if (request is GXDeviceProfilesRequest)
            {
                target = ActionTargets.DeviceProfile;
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
#if !SS4
        public override object HandleException(IRequestContext requestContext, TRequest request, Exception ex)
#else
        public override object HandleException(IRequest requestContext, TRequest request, Exception ex)
#endif        
        {
            //Do not handle connection close exceptions.
            if (ex is System.Net.WebException && (ex as System.Net.WebException).Status != System.Net.WebExceptionStatus.ConnectionClosed)
            {
                try
                {
                    GuruxAMI.Server.AppHost.ReportError(ex);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine(ex2.Message);
                }
                try
                {
                    if (!System.Diagnostics.EventLog.SourceExists("GuruxAMI"))
                    {
                        System.Diagnostics.EventLog.CreateEventSource("GuruxAMI", "Application");
                    }
                    System.Diagnostics.EventLog appLog = new System.Diagnostics.EventLog();
                    appLog.Source = "GuruxAMI";
                    appLog.WriteEntry(ex.Message);
                }
                catch (System.Security.SecurityException)
                {
                    //Security exception is thrown if GuruxAMI source is not exists and it's try to create without administrator privilege.
                    //Just skip this, but errors are not write to eventlog.
                }

                try
                {
#if !SS4
                    var httpReq = requestContext.Get<IHttpRequest>();
#else
                    var httpReq = request.GetDto<IHttpRequest>();
#endif


                    int id = 0;
                    if (int.TryParse(httpReq.GetSession(false).Id, out id) && id != 0)
                    {
                        ActionTargets target;
                        Actions action;
                        GetExecutedAction(request, out target, out action);
#if !SS4
                        IDbConnectionFactory f = this.TryResolve<IDbConnectionFactory>();
#else
                        IDbConnectionFactory f = this.ResolveService<IDbConnectionFactory>(requestContext);
#endif
                        using (IDbConnection Db = f.OpenDbConnection())
                        {
                            lock (Db)
                            {
                                GXAmiSystemError e = new GXAmiSystemError(id, target, action, ex);
                                Db.Insert(e);
                            }
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
