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
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;

namespace GuruxAMI.Server
{
	class GXBasicAuthProvider : BasicAuthProvider
	{
        /// <summary>
        /// Constructor.
        /// </summary>
        public GXBasicAuthProvider()
        {
        }

        public override void OnFailedAuthentication(IAuthSession session, ServiceStack.ServiceHost.IHttpRequest httpReq, ServiceStack.ServiceHost.IHttpResponse httpRes)
        {
            base.OnFailedAuthentication(session, httpReq, httpRes);
        }
        public override object Logout(IServiceBase service, Auth request)
        {
            return base.Logout(service, request);
        }

        static public bool IsSuperAdmin(IAuthSession s)
        {
            return Convert.ToInt32(s.Roles[0]) == (int) UserAccessRights.SuperAdmin;
        }

        /// <summary>
        /// Can user, add or remove users.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static public bool CanUserEdit(IAuthSession s)
        {
            int value = Convert.ToInt32(s.Roles[0]);
            return (value & (int) UserAccessRights.UserAdmin) == (int) UserAccessRights.UserAdmin ||
                (value & (int) UserAccessRights.SuperAdmin) == (int) UserAccessRights.SuperAdmin;
        }

        /// <summary>
        /// Can user, add or remove devices.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static public bool CanEditDevice(IAuthSession s)
        {
            int value = Convert.ToInt32(s.Roles[0]);
            return (value & (int)UserAccessRights.DeviceAdmin) == (int)UserAccessRights.DeviceAdmin ||
                (value & (int)UserAccessRights.SuperAdmin) == (int)UserAccessRights.SuperAdmin;
        }

        void InitUser(IServiceBase authService, GXAmiUser user)
        {
            IAuthSession s = authService.GetSession(false);
            s.Id = user.Id.ToString();
            s.UserAuthId = Guid.NewGuid().ToString();
            s.UserName = user.Name;
            s.IsAuthenticated = true;
            s.Roles = new List<string>();
            s.Roles.Add(user.AccessRightsAsInt.ToString());
        }

        public static bool IsGuid(string possibleGuid, out Guid guid)
        {
            try
            {
                guid = new Guid(possibleGuid);
                return true;
            }
            catch (Exception)
            {
                guid = Guid.Empty;
                return false;
            }
        }

        /// <summary>
        /// Is user allowed to use service.
        /// </summary>
        /// <param name="authService"></param>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public override bool TryAuthenticate(IServiceBase authService, string userName, string password)
        {
            GXAmiUser user;
            IDbConnectionFactory f = authService.TryResolve<IDbConnectionFactory>();
            //Connection factory is null when we are configure server at first time.
            if (f == null)
            {
                return true;
            }
            try
            {                
                using (IDbConnection Db = f.OpenDbConnection())
                {                    
                    if (!GuruxAMI.Service.GXManagementService.IsDatabaseCreated(Db))
                    {
                        string[] items = ServiceStack.ServiceHost.RestPath.GetPathPartsForMatching(authService.RequestContext.PathInfo);
                        string target = items[items.Length - 1];
                        if (string.Compare(target, typeof(GXIsDatabaseCreatedRequest).Name, true) == 0 ||
                            string.Compare(target, typeof(GXCreateTablesRequest).Name, true) == 0 ||
                            string.Compare(target, typeof(GXDropTablesRequest).Name, true) == 0)
                        {
                            user = new GXAmiUser("gurux", "gurux", UserAccessRights.SuperAdmin);
                            user.Id = 1;
                            InitUser(authService, user);
                            return true;
                        }
                        return false;
                    }
                    List<GXAmiUser> users = Db.Select<GXAmiUser>(q => q.Name == userName && q.Password == password);
                    if (users.Count != 1)
                    {
                        //If known DC try to get new tasks, add new task, mark task claimed or add device exception.
                        Guid guid;
                        string[] items = ServiceStack.ServiceHost.RestPath.GetPathPartsForMatching(authService.RequestContext.PathInfo);
                        string target = items[items.Length - 1];
                        if (items != null && items.Length != 0)
                        {
                            if (string.Compare(target, typeof(GXEventsRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXEventsRegisterRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXEventsUnregisterRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXTaskDeleteRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXTasksRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXTaskUpdateRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXTasksClaimRequest).Name, true) == 0 ||
                                string.Compare(target, typeof(GXTraceLevelRequest).Name, true) == 0)
                            {
                                //If DC register first time and starts to listen events.
                                if (IsGuid(userName, out guid))
                                {
                                    //If known DC wants to listen events.
                                    List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(p => p.Guid == guid);
                                    if (list.Count == 1)
                                    {
                                        IAuthSession s = authService.GetSession(false);
                                        s.Id = userName;
                                        s.UserAuthId = Guid.NewGuid().ToString();
                                        s.UserName = userName;
                                        s.IsAuthenticated = true;
                                        s.Roles = new List<string>();
                                        s.Roles.Add("0");
                                        return true;
                                    }
                                    return false;
                                }
                                return false;
                            }
                            else if (string.Compare(target, typeof(GXDataCollectorUpdateRequest).Name, true) == 0 &&
                                IsGuid(userName, out guid))
                            {
                                if (guid == Guid.Empty)
                                {
                                    /* TODO:
                                    IAuthSession s = authService.GetSession(false);
                                    s.Id = userName;
                                    s.UserAuthId = Guid.NewGuid().ToString();
                                    s.UserName = userName;
                                    s.IsAuthenticated = true;
                                    s.Roles = new List<string>();
                                    s.Roles.Add("0");
                                     * */
                                    return true;
                                }
                                //If DC updates itself.
                                List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(p => p.Guid == guid);
                                if (list.Count == 1)
                                {
                                    IAuthSession s = authService.GetSession(false);
                                    s.Id = userName;
                                    s.UserAuthId = Guid.NewGuid().ToString();
                                    s.UserName = userName;
                                    s.IsAuthenticated = true;
                                    s.Roles = new List<string>();
                                    s.Roles.Add("0");
                                    return true;
                                }
                                return false;
                            }
                            return false;
                        }
                    }
                    user = users[0];
                    InitUser(authService, user);
                }
            }
            catch (System.OverflowException)
            {
                throw new Exception("Install new version from MySQL Connector.");
                //There is a bug in MySQL Connector. Version 6.5.4.0 is not working. You must use version 6.1.6.
                //http://www.mysql.com/products/connector/
            }
            return true;
        }
    }
}
