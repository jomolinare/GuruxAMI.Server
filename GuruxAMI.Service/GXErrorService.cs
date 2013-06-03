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
            return new GXErrorsResponse((GuruxAMI.Common.GXAmiSystemError[]) null);
		}
		public GXErrorDeleteResponse Delete(GXErrorDeleteRequest request)
		{
			return new GXErrorDeleteResponse();
		}
	}
}
