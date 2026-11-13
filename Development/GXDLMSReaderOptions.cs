// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Supplies the initial configuration and callbacks for one meter session.</summary>
    /// <remarks>
    /// The reader copies these settings during construction. Changing this object afterward
    /// does not reconfigure that reader. Integer time values are expressed in milliseconds.
    /// </remarks>
    public sealed class GXDLMSReaderOptions
    {
        /// <summary>Gets or sets the positive complete-packet timeout outside connection establishment in milliseconds; defaults to 5000.</summary>
        public int WaitTime { get; set; } = 5000;
        /// <summary>Gets or sets the positive maximum number of attempts for an explicitly rejected packet; defaults to three.</summary>
        /// <remarks>Response timeouts are not retried automatically because the meter may already have executed the request.</remarks>
        public int RetryCount { get; set; } = 3;
        /// <summary>Gets or sets the positive deadline for a complete certificate-service HTTP request, including its response body; defaults to 100 seconds.</summary>
        /// <remarks>The maximum supported duration is 4,294,967,294 milliseconds. Caller cancellation can end the request earlier.</remarks>
        public TimeSpan CertificateServiceTimeout { get; set; } = TimeSpan.FromSeconds(100);
        /// <summary>Gets or sets the positive maximum certificate-service JSON response size in bytes; defaults to 1 MiB (1,048,576 bytes).</summary>
        /// <remarks>The limit applies even when the server omits Content-Length or uses chunked transfer encoding.</remarks>
        public int CertificateServiceMaxResponseBytes { get; set; } = 1024 * 1024;
        /// <summary>Gets or sets the nonnegative minimum interval between transmitted requests in milliseconds.</summary>
        public int FrameDelay { get; set; }
        /// <summary>Gets or sets whether textual diagnostics are delivered to <see cref="GXDLMSReader.OnTrace"/>; defaults to disabled.</summary>
        public TraceLevel TraceLevel { get; set; } = TraceLevel.Off;
        /// <summary>Optional host-specific optical initialization after opening the media.</summary>
        /// <remarks>
        /// The callback receives the current operation's cancellation token and replaces
        /// the built-in optical-head initialization. It may run again during counter discovery or reconnect.
        /// This callback executes synchronously without a guaranteed UI context; prefer
        /// <see cref="InitializeMediaAsync"/> for asynchronous I/O. Do not set both initializers.
        /// </remarks>
        public Action<CancellationToken>? InitializeMedia { get; set; }
        /// <summary>Gets or sets an asynchronous host initializer awaited after opening the media and before association requests.</summary>
        /// <remarks>
        /// Replaces built-in optical initialization and may run again during counter discovery or reconnect.
        /// Set either this property or <see cref="InitializeMedia"/>, never both. The reader snapshots the callback at construction.
        /// The callback runs under the session gate without a guaranteed UI context; use the supplied cancellation
        /// token and bounded I/O timeouts, and do not call reader session methods from the callback.
        /// </remarks>
        public Func<CancellationToken, Task>? InitializeMediaAsync { get; set; }
        /// <summary>True only when the caller has supplied a previously read association view.</summary>
        public bool AssociationViewLoaded { get; set; }
        /// <summary>Local checks only. The meter always enforces its own permissions.</summary>
        public bool CheckAccessRights { get; set; } = true;
    }
}
