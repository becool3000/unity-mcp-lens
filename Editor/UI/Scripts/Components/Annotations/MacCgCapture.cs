using System;
using System.Runtime.InteropServices;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Utils;
// ReSharper disable InconsistentNaming

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
#if UNITY_EDITOR_OSX
    /// <summary>
    /// macOS-specific screen capture implementation using Core Graphics.
    /// </summary>
    static class MacCgCapture
    {
        const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string LibSystem = "/usr/lib/libSystem.B.dylib";

        const uint k_CgWindowListOptionAll = 0;
        const uint k_CgWindowListOptionOnScreenOnly = 1;
        const uint k_CgWindowListOptionIncludingWindow = 8;

        const uint k_CgWindowImageDefault = 0;
        const uint k_CgWindowImageBestResolution = 8;

        const uint k_CgBitmapByteOrder32Little = 0x2000;
        const uint k_CgImageAlphaPremultipliedFirst = 2;
        const uint k_BitmapInfoBgra = k_CgBitmapByteOrder32Little | k_CgImageAlphaPremultipliedFirst;

        // CoreFoundation Encoding Constants
        const int k_CfStringEncodingUtf8 = 0x08000100;

        public static byte[] CaptureDesktopRGBA(out int width, out int height)
        {
            CGRect union = GetActiveDisplayUnionBounds();

            IntPtr img = CGWindowListCreateImage(union, k_CgWindowListOptionOnScreenOnly, 0, k_CgWindowImageDefault | k_CgWindowImageBestResolution);
            if (img == IntPtr.Zero) throw new Exception("CGWindowListCreateImage failed (desktop).");

            try
            {
                return CGImageToRgba(img, out width, out height);
            }
            finally
            {
                CGImageRelease(img);
            }
        }

        public static byte[] CaptureMainMonitorRGBA(out int width, out int height)
        {
            // On macOS, capture the display that contains the main editor window
            uint windowId = FindMainWindowIdForPid(getpid());
            if (windowId == 0)
            {
                // Fallback to capturing all displays if we can't find the editor window
                InternalLog.LogWarning("[EditScreenCapture] Could not find Unity Editor window, falling back to full screen capture");
                return CaptureDesktopRGBA(out width, out height);
            }

            // Get the window bounds to determine which display it's on
            IntPtr windowDict = CGWindowListCopyWindowInfo(k_CgWindowListOptionIncludingWindow, windowId);
            if (windowDict == IntPtr.Zero)
            {
                InternalLog.LogWarning("[EditScreenCapture] Could not get window info, falling back to full screen capture");
                return CaptureDesktopRGBA(out width, out height);
            }

            try
            {
                // Extract the window bounds from the dictionary
                IntPtr keyBounds = CFString("kCGWindowBounds");
                IntPtr keyX = CFString("X");
                IntPtr keyY = CFString("Y");
                IntPtr keyW = CFString("Width");
                IntPtr keyH = CFString("Height");

                try
                {
                    // [FIX 1] Check array count before accessing index 0 to prevent crash
                    if (CFArrayGetCount(windowDict) == 0)
                    {
                        InternalLog.LogWarning("[EditScreenCapture] Window info array is empty, falling back to full screen capture");
                        return CaptureDesktopRGBA(out width, out height);
                    }

                    IntPtr dict = CFArrayGetValueAtIndex(windowDict, 0);
                    if (dict == IntPtr.Zero)
                    {
                        InternalLog.LogWarning("[EditScreenCapture] Could not extract window bounds, falling back to full screen capture");
                        return CaptureDesktopRGBA(out width, out height);
                    }

                    IntPtr boundsDict = CFDictionaryGetValue(dict, keyBounds);
                    double windowX = CFDictGetDouble(boundsDict, keyX, 0);
                    double windowY = CFDictGetDouble(boundsDict, keyY, 0);
                    double windowWidth = CFDictGetDouble(boundsDict, keyW, 0);
                    double windowHeight = CFDictGetDouble(boundsDict, keyH, 0);

                    CGRect windowBounds = new CGRect(windowX, windowY, windowWidth, windowHeight);

                    // Find the display that contains the editor window by checking the window bounds
                    CGGetActiveDisplayList(0, null, out uint displayCount);
                    if (displayCount == 0)
                    {
                        InternalLog.LogWarning("[EditScreenCapture] No displays found, falling back to full screen capture");
                        return CaptureDesktopRGBA(out width, out height);
                    }

                    uint[] displays = new uint[displayCount];
                    CGGetActiveDisplayList(displayCount, displays, out displayCount);

                    // Find which display contains the window center
                    uint targetDisplay = displays[0];
                    if (windowBounds.Width > 0 && windowBounds.Height > 0)
                    {
                        double windowCenterX = windowBounds.X + windowBounds.Width / 2.0;
                        double windowCenterY = windowBounds.Y + windowBounds.Height / 2.0;

                        for (int i = 0; i < displays.Length; i++)
                        {
                            CGRect displayBounds = CGDisplayBounds(displays[i]);
                            if (windowCenterX >= displayBounds.X && windowCenterX < displayBounds.X + displayBounds.Width &&
                                windowCenterY >= displayBounds.Y && windowCenterY < displayBounds.Y + displayBounds.Height)
                            {
                                targetDisplay = displays[i];
                                break;
                            }
                        }
                    }

                    CGRect captureDisplayBounds = CGDisplayBounds(targetDisplay);

                    IntPtr img = CGWindowListCreateImage(captureDisplayBounds, k_CgWindowListOptionOnScreenOnly, 0, k_CgWindowImageDefault | k_CgWindowImageBestResolution);
                    if (img == IntPtr.Zero) throw new Exception("CGWindowListCreateImage failed (main display).");

                    try
                    {
                        return CGImageToRgba(img, out width, out height);
                    }
                    finally
                    {
                        CGImageRelease(img);
                    }
                }
                finally
                {
                    CFRelease(keyBounds);
                    CFRelease(keyX);
                    CFRelease(keyY);
                    CFRelease(keyW);
                    CFRelease(keyH);
                }
            }
            finally
            {
                CFRelease(windowDict);
            }
        }

        public static byte[] CaptureUnityEditorWindowRGBA(out int width, out int height)
        {
            uint windowId = FindMainWindowIdForPid(getpid());
            if (windowId == 0) throw new Exception("Could not find Unity Editor window id.");

            IntPtr img = CGWindowListCreateImage(CGRect.Null, k_CgWindowListOptionIncludingWindow, windowId, k_CgWindowImageDefault | k_CgWindowImageBestResolution);
            if (img == IntPtr.Zero) throw new Exception("CGWindowListCreateImage failed (window).");

            try
            {
                return CGImageToRgba(img, out width, out height);
            }
            finally
            {
                CGImageRelease(img);
            }
        }

        static byte[] CGImageToRgba(IntPtr cgImage, out int width, out int height)
        {
            width = (int)CGImageGetWidth(cgImage);
            height = (int)CGImageGetHeight(cgImage);

            int bytesPerRow = width * 4;
            byte[] bgra = new byte[bytesPerRow * height];

            GCHandle handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            IntPtr colorSpace = CGColorSpaceCreateDeviceRGB();

            try
            {
                IntPtr ctx = CGBitmapContextCreate(
                    handle.AddrOfPinnedObject(),
                    (nuint)width,
                    (nuint)height,
                    8,
                    (nuint)bytesPerRow,
                    colorSpace,
                    k_BitmapInfoBgra);

                if (ctx == IntPtr.Zero) throw new Exception("CGBitmapContextCreate failed.");

                try
                {
                    CGContextTranslateCTM(ctx, 0, height);
                    CGContextScaleCTM(ctx, 1, -1);
                    CGContextDrawImage(ctx, new CGRect(0, 0, width, height), cgImage);
                }
                finally
                {
                    CGContextRelease(ctx);
                }
            }
            finally
            {
                CGColorSpaceRelease(colorSpace);
                handle.Free();
            }

            // BGRA -> RGBA, alpha -> 255
            for (int i = 0; i < bgra.Length; i += 4)
            {
                byte b = bgra[i + 0];
                byte r = bgra[i + 2];
                bgra[i + 0] = r;
                bgra[i + 2] = b;
                bgra[i + 3] = 255;
            }

            return bgra;
        }

        static CGRect GetActiveDisplayUnionBounds()
        {
            CGGetActiveDisplayList(0, null, out uint count);
            if (count == 0) return new CGRect(0, 0, 1, 1);

            uint[] displays = new uint[count];
            CGGetActiveDisplayList(count, displays, out count);

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                CGRect b = CGDisplayBounds(displays[i]);
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.X + b.Width);
                maxY = Math.Max(maxY, b.Y + b.Height);
            }

            return new CGRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        static string GetCFStringValue(IntPtr cfString)
        {
            if (cfString == IntPtr.Zero) return null;
        
            // Fast path: Get C-string pointer (UTF-8)
            IntPtr cStr = CFStringGetCStringPtr(cfString, k_CfStringEncodingUtf8);
            if (cStr != IntPtr.Zero)
            {
                // Unity 6000+ supports this directly
                return Marshal.PtrToStringUTF8(cStr);
            }
        
            // Fallback: use CFStringGetLength + CFStringGetCharacters
            // (Required because CFStringGetCStringPtr can return NULL for non-C-string backed CFStrings)
            nint length = CFStringGetLength(cfString);
            if (length == 0) return string.Empty;
        
            // Note: On Unity 6000+ (C# 9.0+), 'nint' is implicitly convertible to 'int' for array sizes.
            // Explicit casting is not required here for compilation.
            char[] buffer = new char[length];
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                CFStringGetCharacters(cfString, new CFRange(0, length), handle.AddrOfPinnedObject());
                return new string(buffer);
            }
            finally
            {
                handle.Free();
            }
        }

        static uint FindMainWindowIdForPid(int pid)
        {
            IntPtr arr = CGWindowListCopyWindowInfo(k_CgWindowListOptionAll, 0);
            if (arr == IntPtr.Zero) return 0;

            IntPtr keyOwnerPid = CFString("kCGWindowOwnerPID");
            IntPtr keyWindowNumber = CFString("kCGWindowNumber");
            IntPtr keyLayer = CFString("kCGWindowLayer");
            IntPtr keyBounds = CFString("kCGWindowBounds");
            IntPtr keyW = CFString("Width");
            IntPtr keyH = CFString("Height");
            IntPtr keyName = CFString("kCGWindowName");

            try
            {
                nint n = CFArrayGetCount(arr);
                uint bestId = 0;
                double bestArea = 0;

                for (nint i = 0; i < n; i++)
                {
                    IntPtr dict = CFArrayGetValueAtIndex(arr, i);
                    if (dict == IntPtr.Zero) continue;

                    int ownerPid = (int)CFDictGetInt64(dict, keyOwnerPid, 0);
                    if (ownerPid != pid) continue;

                    int layer = (int)CFDictGetInt64(dict, keyLayer, 0);
                    if (layer != 0) continue;

                    uint winId = (uint)CFDictGetInt64(dict, keyWindowNumber, 0);
                    if (winId == 0) continue;

                    // Skip floating Assistant window by checking title
                    string windowTitle = GetCFStringValue(CFDictionaryGetValue(dict, keyName));
                    if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains("Assistant"))
                    {
                        continue;
                    }

                    IntPtr boundsDict = CFDictionaryGetValue(dict, keyBounds);
                    double w = CFDictGetDouble(boundsDict, keyW, 0);
                    double h = CFDictGetDouble(boundsDict, keyH, 0);
                    double area = w * h;

                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestId = winId;
                    }
                }

                return bestId;
            }
            finally
            {
                CFRelease(keyOwnerPid);
                CFRelease(keyWindowNumber);
                CFRelease(keyLayer);
                CFRelease(keyBounds);
                CFRelease(keyW);
                CFRelease(keyH);
                CFRelease(keyName);
                CFRelease(arr);
            }
        }

        static long CFDictGetInt64(IntPtr dict, IntPtr key, long fallback)
        {
            if (dict == IntPtr.Zero || key == IntPtr.Zero) return fallback;
            IntPtr val = CFDictionaryGetValue(dict, key);
            if (val == IntPtr.Zero) return fallback;

            long outVal = 0;
            if (!CFNumberGetValue(val, k_CfNumberSInt64Type, out outVal)) return fallback;
            return outVal;
        }

        static double CFDictGetDouble(IntPtr dict, IntPtr key, double fallback)
        {
            if (dict == IntPtr.Zero || key == IntPtr.Zero) return fallback;
            IntPtr val = CFDictionaryGetValue(dict, key);
            if (val == IntPtr.Zero) return fallback;

            double outVal = 0;
            if (!CFNumberGetValue_Double(val, k_CfNumberDoubleType, out outVal)) return fallback;
            return outVal;
        }

        static IntPtr CFString(string s)
        {
            return CFStringCreateWithCString(IntPtr.Zero, s, k_CfStringEncodingUtf8);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CGPoint { public double x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct CGSize { public double width, height; }

        [StructLayout(LayoutKind.Sequential)]
        struct CGRect
        {
            public CGPoint origin;
            public CGSize size;

            public double X => origin.x;
            public double Y => origin.y;
            public double Width => size.width;
            public double Height => size.height;

            public CGRect(double x, double y, double w, double h)
            {
                origin.x = x; origin.y = y;
                size.width = w; size.height = h;
            }

            public static CGRect Null => new CGRect(double.PositiveInfinity, double.PositiveInfinity, 0, 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CFRange
        {
            public nint location;
            public nint length;

            public CFRange(nint loc, nint len)
            {
                location = loc;
                length = len;
            }
        }

        const int k_CfNumberSInt64Type = 4;
        const int k_CfNumberDoubleType = 13;

        [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowID, uint imageOption);
        [DllImport(CoreGraphics)] static extern void CGImageRelease(IntPtr image);
        [DllImport(CoreGraphics)] static extern nuint CGImageGetWidth(IntPtr image);
        [DllImport(CoreGraphics)] static extern nuint CGImageGetHeight(IntPtr image);

        [DllImport(CoreGraphics)] static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] activeDisplays, out uint displayCount);
        [DllImport(CoreGraphics)] static extern CGRect CGDisplayBounds(uint display);

        [DllImport(CoreGraphics)] static extern IntPtr CGColorSpaceCreateDeviceRGB();
        [DllImport(CoreGraphics)] static extern void CGColorSpaceRelease(IntPtr space);

        [DllImport(CoreGraphics)]
        static extern IntPtr CGBitmapContextCreate(
            IntPtr data,
            nuint width,
            nuint height,
            nuint bitsPerComponent,
            nuint bytesPerRow,
            IntPtr space,
            uint bitmapInfo);

        [DllImport(CoreGraphics)] static extern void CGContextRelease(IntPtr c);
        [DllImport(CoreGraphics)] static extern void CGContextDrawImage(IntPtr c, CGRect rect, IntPtr image);
        [DllImport(CoreGraphics)] static extern void CGContextTranslateCTM(IntPtr c, double tx, double ty);
        [DllImport(CoreGraphics)] static extern void CGContextScaleCTM(IntPtr c, double sx, double sy);

        [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

        [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFoundation)] static extern nint CFArrayGetCount(IntPtr theArray);
        [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, nint idx);
        [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);

        [DllImport(CoreFoundation)]
        static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

        [DllImport(CoreFoundation)]
        static extern bool CFNumberGetValue(IntPtr number, int theType, out long value);

        [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")]
        static extern bool CFNumberGetValue_Double(IntPtr number, int theType, out double value);

        [DllImport(CoreFoundation)]
        static extern IntPtr CFStringGetCStringPtr(IntPtr theString, int encoding);

        [DllImport(CoreFoundation)]
        static extern nint CFStringGetLength(IntPtr theString);

        [DllImport(CoreFoundation)]
        static extern void CFStringGetCharacters(IntPtr theString, CFRange range, IntPtr buffer);

        [DllImport(LibSystem)] static extern int getpid();
    }
#endif
}
