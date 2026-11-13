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

using Gurux.DLMS.ASN;
using Gurux.DLMS.Objects.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Submits signing requests to a Gurux-compatible HTTP certificate service and validates its responses.</summary>
    internal static class GXCertificateService
    {

        /// <summary>Shares pooled HTTP connections across signing requests while allowing DNS changes after five minutes.</summary>
        private static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseCookies = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>Rejects certificate-service limits unsupported by bounded response reads or cancellation timers.</summary>
        /// <param name="timeout">The positive complete-request deadline.</param>
        /// <param name="maxResponseBytes">The positive maximum JSON response size in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">The deadline is nonpositive or exceeds the timer range, or the response size is nonpositive.</exception>
        internal static void ValidateSettings(TimeSpan timeout, int maxResponseBytes)
        {
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);
        }

        /// <summary>Submits Gurux-compatible signing requests with cancellable HTTP I/O and validates returned public keys.</summary>
        /// <param name="address">Certificate-generator HTTP endpoint.</param>
        /// <param name="requests">Ordered signing requests whose keys must match the returned certificates.</param>
        /// <param name="cancellationToken">Token that cancels submission, response reading and validation.</param>
        /// <param name="timeout">Complete HTTP request deadline, including response reading; null selects 100 seconds.</param>
        /// <param name="maxResponseBytes">Positive maximum response size in bytes, including JSON framing; defaults to 1 MiB.</param>
        /// <returns>Signed certificates in request order.</returns>
        /// <exception cref="OperationCanceledException">The request is canceled or the HTTP timeout expires.</exception>
        /// <exception cref="HttpRequestException">The service cannot be reached or returns an unsuccessful HTTP status.</exception>
        /// <exception cref="InvalidOperationException">The response exceeds its size limit, has no matching certificate array or contains a different public key.</exception>
        /// <exception cref="JsonException">The service response is not valid JSON.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A request uses an unsupported certificate type, the deadline is invalid, or the response size limit is nonpositive.</exception>
        internal static async Task<GXx509Certificate[]> RequestAsync(string address, List<GXCertificateRequest> requests, CancellationToken cancellationToken, TimeSpan? timeout = null, int maxResponseBytes = 1024 * 1024)
        {
            TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(100);
            ValidateSettings(deadline, maxResponseBytes);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellation.CancelAfter(deadline);
            cancellationToken = cancellation.Token;
            cancellationToken.ThrowIfCancellationRequested();
            var certificates = new List<Dictionary<string, object>>();
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int usage = request.CertificateType switch
                {
                    CertificateType.DigitalSignature => (int)Gurux.DLMS.ASN.Enums.KeyUsage.DigitalSignature,
                    CertificateType.KeyAgreement => (int)Gurux.DLMS.ASN.Enums.KeyUsage.KeyAgreement,
                    CertificateType.TLS => (int)(Gurux.DLMS.ASN.Enums.KeyUsage.DigitalSignature | Gurux.DLMS.ASN.Enums.KeyUsage.KeyAgreement),
                    _ => throw new ArgumentOutOfRangeException(nameof(requests))
                };
                var item = new Dictionary<string, object>
                {
                    ["KeyUsage"] = usage,
                    ["CSR"] = request.Certificate.ToDer()
                };
                if (request.ExtendedKeyUsage != Gurux.DLMS.ASN.Enums.ExtendedKeyUsage.None)
                {
                    item["ExtendedKeyUsage"] = (int)request.ExtendedKeyUsage;
                }
                certificates.Add(item);
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, address)
            {
                Content = JsonContent.Create(new Dictionary<string, object> { ["Certificates"] = certificates })
            };
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxResponseBytes)
            {
                throw new InvalidOperationException(Resources.CertificateServiceResponseTooLarge);
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[Math.Min(8192, maxResponseBytes)];
            while (true)
            {
                int count = (int)Math.Min(buffer.Length, (long)maxResponseBytes - body.Length + 1);
                int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (body.Length + read > maxResponseBytes)
                {
                    throw new InvalidOperationException(Resources.CertificateServiceResponseTooLarge);
                }
                body.Write(buffer, 0, read);
            }
            body.Position = 0;
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement values = default;
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "Certificates", StringComparison.OrdinalIgnoreCase))
                    {
                        values = property.Value;
                        break;
                    }
                }
            }
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != requests.Count)
            {
                throw new InvalidOperationException(Resources.CertificateServiceResponseInvalid);
            }

            var result = new GXx509Certificate[requests.Count];
            int index = 0;
            foreach (var value in values.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(Resources.CertificateServiceResponseInvalid);
                }
                var certificate = GXx509Certificate.FromDer(value.GetString());
                if (!certificate.PublicKey.RawValue.SequenceEqual(requests[index].Certificate.PublicKey.RawValue))
                {
                    throw new InvalidOperationException(Resources.CertificateServicePublicKeyMismatch);
                }
                result[index++] = certificate;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

    }
}
