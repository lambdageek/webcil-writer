// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if MSBUILD_TASK
namespace Microsoft.WebAssembly.Build.Tasks.WebCil;
#else
namespace Microsoft.WebAssembly.Webcil.Metadata;
#endif

public static unsafe class Constants
{
    public const int WC_VERSION = 0;
}
