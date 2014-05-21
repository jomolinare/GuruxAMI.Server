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
    /// Service handles error functionality.
    /// </summary>
    [Authenticate]
#if !SS4
    internal class GXSearchService : GXService
#else
    internal class GXSearchService : ServiceStack.Service
#endif
    {
        public GXSearchResponse Post(GXSearchRequest request)
        {
            lock (Db)
            {
                List<object> target = new List<object>();
                IAuthSession s = this.GetSession(false);
                if ((request.Target & ActionTargets.Device) != 0)
                {
                    List<GXAmiDevice> list = GXDeviceService.GetDevices(s, Db, 0, 0, 0, 0, false, request.Texts, request.Operator, request.Type);
                    foreach (GXAmiDevice it in list)
                    {
                        GXDeviceService.UpdateContent(Db, it, DeviceContentType.Main);
                    }
                    target.AddRange(list.ToArray());
                }
                if ((request.Target & ActionTargets.DataCollector) != 0)
                {
                    List<GXAmiDataCollector> list = GXDataCollectorService.GetDataCollectorsByUser(s, Db, 0, 0, false, request.Texts, request.Operator, request.Type);
                    target.AddRange(list.ToArray());
                }                
                if ((request.Target & ActionTargets.User) != 0)
                {
                    List<GXAmiUser> list = GXUserService.GetUsers(s, Db, 0, 0, false, true, request.Texts, request.Operator, request.Type);
                    target.AddRange(list.ToArray());
                }
                if ((request.Target & ActionTargets.UserGroup) != 0)
                {
                    List<GXAmiUserGroup> list = GXUserGroupService.GetUserGroups(Db, 0, request.Texts, request.Operator, request.Type);
                    target.AddRange(list.ToArray());
                }
                GXSearchResponse res = new GXSearchResponse(target.ToArray());
                return res;
            }
        }
    }
}
