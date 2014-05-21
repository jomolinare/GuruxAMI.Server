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
#if !SS4
    internal class GXService : ServiceStack.ServiceInterface.Service
#else
    internal class GXService : ServiceStack.Service
#endif    
    {
        public override System.Data.IDbConnection Db
        {
            get
            {
                try
                {                    
                    System.Data.IDbConnection d = base.Db;
                    if (d == null || d.State != System.Data.ConnectionState.Open)
                    {
                        return ReOpenConnection();
                    }
                    else
                    {
                        return d;
                    }
                }
                catch (Exception ex)//Skip error and try to connect again. If reconnect fails error is thrown.
                {
                    GuruxAMI.Server.AppHost.ReportError(ex);
                    return ReOpenConnection();
                }
            }
        }

        /// <summary>
        /// Reopen connection if it's closed.
        /// </summary>
        /// <returns></returns>
        private IDbConnection ReOpenConnection()
        {
            OrmLiteConnectionFactory f = TryResolve<IDbConnectionFactory>() as OrmLiteConnectionFactory;
            if (f != null)
            {
                return f.OpenDbConnection();
            }
            return base.Db;
        }
    }
}
