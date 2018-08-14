﻿using LottieUWP.Model;
using LottieUWP.Parser;
using LottieUWP.Utils;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace LottieUWP
{
    /// <summary>
    /// Helpers to create or cache a LottieComposition.
    /// </summary>
    public static class LottieCompositionFactory
    {
        static LottieCompositionFactory()
        {
            Utils.Utils.DpScale();
        }

        public static async Task<LottieResult<LottieComposition>> FromAsset(CanvasDevice device, string fileName, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                return FromAssetSync(device, fileName);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Name of a files in src/main/assets. If it ends with zip, it will be parsed as a zip file. Otherwise, it will 
        /// be parsed as json.
        /// <see cref="FromZipStreamSync(CanvasDevice, ZipArchive, string)"/>
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static LottieResult<LottieComposition> FromAssetSync(CanvasDevice device, string fileName)
        {
            try
            {
                string cacheKey = "asset_" + fileName;
                if (fileName.EndsWith(".zip"))
                {
                    return FromZipStreamSync(device, new ZipArchive(File.OpenRead(fileName)), cacheKey);
                }
                return FromJsonInputStreamSync(File.OpenRead(fileName), cacheKey);
            }
            catch (IOException e)
            {
                return new LottieResult<LottieComposition>(e);
            }
        }

        /// <summary>
        /// Auto-closes the stream.
        /// <see cref="FromJsonInputStreamSync(Stream, bool"/>
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<LottieResult<LottieComposition>> FromJsonInputStream(Stream stream, string cacheKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                return FromJsonInputStreamSync(stream, cacheKey);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Return a LottieComposition for the given InputStream to json.
        /// </summary>
        public static LottieResult<LottieComposition> FromJsonInputStreamSync(Stream stream, string cacheKey)
        {
            return FromJsonInputStreamSync(stream, cacheKey, true);
        }

        private static LottieResult<LottieComposition> FromJsonInputStreamSync(Stream stream, string cacheKey, bool close)
        {
            try
            {
                return FromJsonReaderSync(new JsonReader(new StreamReader(stream, Encoding.UTF8)), cacheKey);
            }
            finally
            {
                if (close)
                {
                    stream.CloseQuietly();
                }
            }
        }

        /// <summary>
        /// <see cref="FromJsonStringSync(string, string)"/>
        /// </summary>
        /// <param name="json"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<LottieResult<LottieComposition>> FromJsonString(string json, string cacheKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                return FromJsonStringSync(json, cacheKey);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Return a LottieComposition for the specified raw json string. 
        /// If loading from a file, it is preferable to use the InputStream.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static LottieResult<LottieComposition> FromJsonStringSync(string json, string cacheKey)
        {
            return FromJsonReaderSync(new JsonReader(new StringReader(json)), cacheKey);
        }

        public static async Task<LottieResult<LottieComposition>> FromJsonReader(JsonReader reader, string cacheKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                return FromJsonReaderSync(reader, cacheKey);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Return a LottieComposition for the specified json. 
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static LottieResult<LottieComposition> FromJsonReaderSync(JsonReader reader, string cacheKey)
        {
            try
            {
                LottieComposition composition = LottieCompositionParser.Parse(reader);
                LottieCompositionCache.Instance.Put(cacheKey, composition);
                return new LottieResult<LottieComposition>(composition);
            }
            catch (Exception e)
            {
                return new LottieResult<LottieComposition>(e);
            }
        }

        public static async Task<LottieResult<LottieComposition>> FromZipStream(CanvasDevice device, ZipArchive inputStream, string cacheKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                return FromZipStreamSync(device, inputStream, cacheKey);
            }, cancellationToken).ConfigureAwait(false);
        }

        /** 
         * Parses a zip input stream into a Lottie composition. 
         * Your zip file should just be a folder with your json file and images zipped together. 
         * It will automatically store and configure any images inside the animation if they exist. 
         * 
         * It will also close the input stream. 
         */
        private static LottieResult<LottieComposition> FromZipStreamSync(CanvasDevice device, ZipArchive inputStream, string cacheKey)
        {
            try
            {
                return FromZipStreamSyncInternal(device, inputStream, cacheKey);
            }
            finally
            {
                inputStream.CloseQuietly();
            }
        }

        private static LottieResult<LottieComposition> FromZipStreamSyncInternal(CanvasDevice device, ZipArchive inputStream, string cacheKey)
        {
            LottieComposition composition = null;
            Dictionary<string, CanvasBitmap> images = new Dictionary<string, CanvasBitmap>();

            try
            {
                foreach (ZipArchiveEntry entry in inputStream.Entries)
                {
                    if (entry.FullName.Contains("__MACOSX"))
                    {
                        continue;
                    }
                    else if (entry.FullName.Contains(".json"))
                    {
                        composition = FromJsonInputStreamSync(entry.Open(), cacheKey, false).Value;
                    }
                    else if (entry.FullName.Contains(".png"))
                    {
                        string[] splitName = entry.FullName.Split('/');
                        string name = splitName[splitName.Length - 1];
                        using (var stream = AsRandomAccessStream(entry.Open()))
                        {
                            var task = CanvasBitmap.LoadAsync(device, stream, 160).AsTask();
                            task.Wait();
                            var bitmap = task.Result;
                            images[name] = bitmap;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
            }
            catch (IOException e)
            {
                return new LottieResult<LottieComposition>(e);
            }

            if (composition == null)
            {
                return new LottieResult<LottieComposition>(new ArgumentException("Unable to parse composition"));
            }

            foreach (var e in images)
            {
                LottieImageAsset imageAsset = FindImageAssetForFileName(composition, e.Key);
                if (imageAsset != null)
                {
                    imageAsset.Bitmap = e.Value;
                }
            }

            // Ensure that all bitmaps have been set. 
            foreach (var entry in composition.Images)
            {
                if (entry.Value.Bitmap == null)
                {
                    return new LottieResult<LottieComposition>(new ArgumentException("There is no image for " + entry.Value.FileName));
                }
            }

            LottieCompositionCache.Instance.Put(cacheKey, composition);
            return new LottieResult<LottieComposition>(composition);
        }

        private static IRandomAccessStream AsRandomAccessStream(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms.AsRandomAccessStream();
        }

        private static LottieImageAsset FindImageAssetForFileName(LottieComposition composition, string fileName)
        {
            foreach (var asset in composition.Images.Values)
            {
                if (asset.FileName.Equals(fileName))
                {
                    return asset;
                }
            }
            return null;
        }
    }
}