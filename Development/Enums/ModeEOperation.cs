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

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Identifies a host-owned transport operation during IEC 62056-21 Mode E negotiation.</summary>
    public enum ModeEOperation
    {
        /// <summary>Configure 300 baud, seven data bits, even parity and one stop bit before the reader opens the transport.</summary>
        InitModeE,
        /// <summary>Apply the negotiated baud rate with eight data bits, no parity and one stop bit, reopening the transport if needed.</summary>
        SwitchModeE,
        /// <summary>Send the IEC disconnect command and consume its response using the supplied transport.</summary>
        DisconnectModeE,
        /// <summary>Recover after an unanswered identification echo by disabling DTR and RTS, selecting 9600 baud, and enabling DTR and RTS.</summary>
        RecoverModeE
    }
}
