/*
 * This file is based on HuggingfaceHub v0.1.2 (Apache License 2.0)
 * Modifications made by Unity (2025)
 */

using System;
using System.Net.Http.Headers;

#nullable enable
namespace HuggingfaceHub.Common
{
    record HfFileMetadata(
        string? CommitHash,
        EntityTagHeaderValue? Etag,
        Uri? Location,
        long? Size
    );
}
