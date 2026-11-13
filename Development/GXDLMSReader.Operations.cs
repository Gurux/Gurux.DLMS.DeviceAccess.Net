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

using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using System;
using System.Collections.Generic;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {

        /// <summary>Validates a target and its one-based attribute or method index against the COSEM object definition.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="index">One-based attribute or method index.</param>
        /// <param name="method">Whether the index addresses a method instead of an attribute.</param>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside the target definition.</exception>
        private static void ValidateAttribute(GXDLMSObject target, int index, bool method = false)
        {
            ArgumentNullException.ThrowIfNull(target);
            int count = method ? ((IGXDLMSBase)target).GetMethodCount() : ((IGXDLMSBase)target).GetAttributeCount();
            if (index < 1 || index > count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>Validates an index and optionally checks local association rights for a read, write or method invocation.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="index">One-based attribute or method index.</param>
        /// <param name="check">Local rights-check override; null uses CheckAccessRights.</param>
        /// <param name="operation">Operation selector: zero for read, one for write, or two for method invocation.</param>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        private void EnsureAccess(GXDLMSObject target, int index, bool? check, int operation)
        {
            ValidateAttribute(target, index, operation == 2);
            if (check ?? CheckAccessRights)
            {
                bool allowed = operation == 0 ? Client.CanRead(target, index) : operation == 1 ? Client.CanWrite(target, index) : Client.CanInvoke(target, index);
                if (!allowed)
                {
                    throw new UnauthorizedAccessException(string.Format(CultureInfo.CurrentCulture, operation == 0 ? Resources.ReadAccessDenied : operation == 1 ? Resources.WriteAccessDenied : Resources.MethodAccessDenied, target.LogicalName, index));
                }
            }
        }

        /// <summary>Reads an attribute and updates its cached value and, if unknown, its data type on the target object.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The decoded attribute value after updating the target object.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public object? Read(GXDLMSObject target, int attributeIndex, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => ReadAsync(target, attributeIndex, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Reads an attribute and updates its cached value and, if unknown, its data type on the target object.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded attribute value after updating the target object.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public async Task<object?> ReadAsync(GXDLMSObject target, int attributeIndex, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            EnsureAccess(target, attributeIndex, checkAccessRights, 0);
            var reply = new GXReplyData();
            await ReadDataBlockAsync(Client.Read(target, attributeIndex), reply).ConfigureAwait(false);
            if (target.GetDataType(attributeIndex) == DataType.None)
            {
                target.SetDataType(attributeIndex, reply.DataType);
            }

            return Client.UpdateValue(target, attributeIndex, reply.Value);
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Synchronously writes the value already set on the COSEM object and clears its dirty flag after acceptance.</summary>
        /// <remarks>This method blocks until the asynchronous write completes. The cancellation token precedes the optional rights override so a positional boolean remains an attribute value. Use a named checkAccessRights argument to change the local policy. A third-position default literal selects this overload; use value: default to explicitly write a null candidate.</remarks>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public void Write(GXDLMSObject target, int attributeIndex, CancellationToken cancellationToken = default, bool? checkAccessRights = null) => WriteAsync(target, attributeIndex, cancellationToken, checkAccessRights).GetAwaiter().GetResult();
        /// <summary>Writes the value already set on the COSEM object and clears its dirty flag after acceptance.</summary>
        /// <remarks>The cancellation token precedes the optional rights override so a positional boolean remains an attribute value. Use a named checkAccessRights argument to change the local policy. A third-position default literal selects this overload; use value: default to explicitly write a null candidate.</remarks>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task WriteAsync(GXDLMSObject target, int attributeIndex, CancellationToken cancellationToken = default, bool? checkAccessRights = null) => await ExecuteAsync(async () =>
        {
            EnsureAccess(target, attributeIndex, checkAccessRights, 1);
            await ReadDataBlockAsync(Client.Write(target, attributeIndex), new GXReplyData()).ConfigureAwait(false);
            target.ClearDirty(attributeIndex);
        }, cancellationToken).ConfigureAwait(false);

        /// <summary>Writes a candidate value using a cloned object, updating the original only after the meter accepts it. Boolean values are candidate values, not access-rights overrides.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="value">Attribute value or method argument; null is passed to the COSEM setter or method encoder when supported by the target.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public void Write(GXDLMSObject target, int attributeIndex, object? value, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => WriteAsync(target, attributeIndex, value, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Writes a candidate value using a cloned object, updating the original only after the meter accepts it. Boolean values are candidate values, not access-rights overrides.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="attributeIndex">One-based attribute index on the target object.</param>
        /// <param name="value">Attribute value or method argument; null is passed to the COSEM setter or method encoder when supported by the target.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task WriteAsync(GXDLMSObject target, int attributeIndex, object? value, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            EnsureAccess(target, attributeIndex, checkAccessRights, 1);
            var candidate = target.Clone();
            SetUserValue(candidate, attributeIndex, value);
            await ReadDataBlockAsync(Client.Write(candidate, attributeIndex), new GXReplyData()).ConfigureAwait(false);
            SetUserValue(target, attributeIndex, value);
            target.ClearDirty(attributeIndex);
        }, cancellationToken).ConfigureAwait(false);

        /// <summary>Applies a user-supplied attribute value through the COSEM object setter and reports any DLMS validation error.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="index">One-based attribute or method index.</param>
        /// <param name="value">Attribute value or method argument; null is passed to the COSEM setter or method encoder when supported by the target.</param>
        /// <exception cref="GXDLMSException">The COSEM setter rejects the value.</exception>
        private void SetUserValue(GXDLMSObject target, int index, object? value)
        {
            var args = new ValueEventArgs(target, index, 0, null)
            {
                Value = value,
                User = true
            };
            ((IGXDLMSBase)target).SetValue(Client.Settings, args);
            if (args.Error != ErrorCode.Ok)
            {
                throw new GXDLMSException((int)args.Error);
            }
        }

        /// <summary>Invokes a COSEM method and returns the decoded response value.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="methodIndex">One-based method index on the target object.</param>
        /// <param name="value">Attribute value or method argument; null is passed to the COSEM setter or method encoder when supported by the target.</param>
        /// <param name="type">DLMS data type used to encode the method argument.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The decoded method response value, or null when the action returns no value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public object? Method(GXDLMSObject target, int methodIndex, object? value, DataType type, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => MethodAsync(target, methodIndex, value, type, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Invokes a COSEM method and returns the decoded response value.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="methodIndex">One-based method index on the target object.</param>
        /// <param name="value">Attribute value or method argument; null is passed to the COSEM setter or method encoder when supported by the target.</param>
        /// <param name="type">DLMS data type used to encode the method argument.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded method response value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public async Task<object?> MethodAsync(GXDLMSObject target, int methodIndex, object? value, DataType type, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await MethodAsync(target, methodIndex, c => c.Method(target, methodIndex, value, type), checkAccessRights, cancellationToken).ConfigureAwait(false);
        /// <summary>Invokes a COSEM method and returns the decoded response value. Generates the method frames inside the serialized session.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="methodIndex">One-based method index on the target object.</param>
        /// <param name="createMessages">Request factory invoked synchronously under the asynchronous session gate with the shared client.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The decoded method response value, or null when the action returns no value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target or request factory is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <exception cref="ArgumentException">The request factory returns null or no frames.</exception>
        public object? Method(GXDLMSObject target, int methodIndex, Func<GXDLMSClient, byte[][]> createMessages, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => MethodAsync(target, methodIndex, createMessages, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Invokes a COSEM method and returns the decoded response value. Generates the method frames inside the serialized session.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="methodIndex">One-based method index on the target object.</param>
        /// <param name="createMessages">Request factory invoked synchronously under the asynchronous session gate with the shared client.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded method response value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target or request factory is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <exception cref="ArgumentException">The request factory returns null or no frames.</exception>
        public async Task<object?> MethodAsync(GXDLMSObject target, int methodIndex, Func<GXDLMSClient, byte[][]> createMessages, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            EnsureAccess(target, methodIndex, checkAccessRights, 2);
            ArgumentNullException.ThrowIfNull(createMessages);
            var messages = createMessages(Client);
            if (messages == null || messages.Length == 0)
            {
                throw new ArgumentException(Resources.ActionRequestMissing, nameof(createMessages));
            }

            var reply = new GXReplyData();
            await ReadDataBlockAsync(messages, reply).ConfigureAwait(false);
            return reply.Value;
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Reads the requested attributes and updates their cached values, using individual reads when multiple references were not negotiated.</summary>
        /// <param name="list">Target objects and one-based attribute indices to process in order.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established, or the response value count differs from the requested attribute count.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public void ReadList(List<KeyValuePair<GXDLMSObject, int>> list, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => ReadListAsync(list, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Reads the requested attributes and updates their cached values, using individual reads when multiple references were not negotiated.</summary>
        /// <param name="list">Target objects and one-based attribute indices to process in order.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established, or the response value count differs from the requested attribute count.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ReadListAsync(List<KeyValuePair<GXDLMSObject, int>> list, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(list);
            foreach (var item in list)
            {
                EnsureAccess(item.Key, item.Value, checkAccessRights, 0);
            }

            if (list.Count == 0)
            {
                return;
            }

            if ((Client.NegotiatedConformance & Conformance.MultipleReferences) == 0)
            {
                foreach (var item in list)
                {
                    await ReadAsync(item.Key, item.Value, checkAccessRights).ConfigureAwait(false);
                }

                return;
            }

            var values = new List<object>();
            foreach (var message in Client.ReadList(list))
            {
                var reply = new GXReplyData();
                await ReadDataBlockAsync(message, reply).ConfigureAwait(false);
                if (reply.Value is IEnumerable<object> result)
                {
                    values.AddRange(result);
                }
            }

            if (values.Count != list.Count)
            {
                throw new InvalidOperationException(Resources.ReadResponseCountMismatch);
            }

            Client.UpdateValues(list, values);
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Writes the current values of the requested attributes, using individual writes when multiple references were not negotiated.</summary>
        /// <param name="list">Target objects and one-based attribute indices to process in order.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <remarks>Individual fallback writes clear dirty flags after acceptance. The multiple-reference write path does not explicitly clear them.</remarks>
        public void WriteList(List<KeyValuePair<GXDLMSObject, int>> list, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => WriteListAsync(list, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Writes the current values of the requested attributes, using individual writes when multiple references were not negotiated.</summary>
        /// <param name="list">Target objects and one-based attribute indices to process in order.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <remarks>Individual fallback writes clear dirty flags after acceptance. The multiple-reference write path does not explicitly clear them.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task WriteListAsync(List<KeyValuePair<GXDLMSObject, int>> list, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(list);
            foreach (var item in list)
            {
                EnsureAccess(item.Key, item.Value, checkAccessRights, 1);
            }

            if (list.Count == 0)
            {
                return;
            }

            if ((Client.NegotiatedConformance & Conformance.MultipleReferences) == 0)
            {
                foreach (var item in list)
                {
                    await WriteAsync(item.Key, item.Value, cancellationToken: operationToken, checkAccessRights: checkAccessRights).ConfigureAwait(false);
                }

                return;
            }

            await ReadDataBlockAsync(Client.WriteList(list), new GXReplyData()).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        /// <summary>Reads profile rows selected by entry index and count and updates the profile buffer on success.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="index">One-based index of the first requested profile row.</param>
        /// <param name="count">Number of profile rows to request, passed unchanged to the DLMS client.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The decoded profile rows stored in the updated profile buffer.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
       
        public object[] ReadRowsByEntry(GXDLMSProfileGeneric target, uint index, uint count, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => ReadRowsByEntryAsync(target, index, count, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Reads profile rows selected by entry index and count and updates the profile buffer on success.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="index">One-based index of the first requested profile row.</param>
        /// <param name="count">Number of profile rows to request, passed unchanged to the DLMS client.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded profile rows stored in the updated profile buffer.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        public async Task<object[]> ReadRowsByEntryAsync(GXDLMSProfileGeneric target, uint index, uint count, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ReadProfileAsync(target, c => c.ReadRowsByEntry(target, index, count), checkAccessRights, cancellationToken).ConfigureAwait(false);
        /// <summary>Reads profile rows selected by a time range and updates the profile buffer on success.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="start">Start of the requested profile time range.</param>
        /// <param name="end">End of the requested profile time range; must not precede start.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The decoded profile rows stored in the updated profile buffer.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <exception cref="ArgumentException">The end of the range precedes its start.</exception>
        public object[] ReadRowsByRange(GXDLMSProfileGeneric target, DateTime start, DateTime end, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => ReadRowsByRangeAsync(target, start, end, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Reads profile rows selected by a time range and updates the profile buffer on success.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="start">Start of the requested profile time range.</param>
        /// <param name="end">End of the requested profile time range; must not precede start.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded profile rows stored in the updated profile buffer.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attribute or method index is outside the target definition.</exception>
        /// <exception cref="UnauthorizedAccessException">The enabled local rights check denies the requested operation.</exception>
        /// <exception cref="ArgumentException">The end of the range precedes its start.</exception>
        public async Task<object[]> ReadRowsByRangeAsync(GXDLMSProfileGeneric target, DateTime start, DateTime end, bool? checkAccessRights = null, CancellationToken cancellationToken = default)
        {
            if (end < start)
            {
                throw new ArgumentException(Resources.InvalidDateRange, nameof(end));
            }

            return (await ReadProfileAsync(target, c => c.ReadRowsByRange(target, start, end), checkAccessRights, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Reads profile buffer attribute 2 with a generated selective-access request, restoring the previous buffer if the operation fails.</summary>
        /// <param name="target">COSEM object containing the requested attribute or method.</param>
        /// <param name="request">Selective-access request factory invoked under the asynchronous session gate.</param>
        /// <param name="checkAccessRights">Local rights-check override; null uses CheckAccessRights. Disabling this check does not change meter-enforced rights.</param>
        /// <param name="token">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the decoded profile rows stored in the updated profile buffer.</returns>
        private async Task<object[]> ReadProfileAsync(GXDLMSProfileGeneric target, Func<GXDLMSClient, byte[][]> request, bool? checkAccessRights, CancellationToken token) => await ExecuteAsync(async () =>
        {
            EnsureAccess(target, 2, checkAccessRights, 0);
            var saved = target.Buffer.ToArray();
            try
            {
                var reply = new GXReplyData();
                await ReadDataBlockAsync(request(Client), reply).ConfigureAwait(false);
                return (object[])Client.UpdateValue(target, 2, reply.Value);
            }
            catch
            {
                target.Buffer.Clear();
                target.Buffer.AddRange(saved);
                throw;
            }
        }, token).ConfigureAwait(false);

    }
}
