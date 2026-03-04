using Cysharp.Threading.Tasks;
//using Newtonsoft.Json;
using SplenSoft.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using UnityEngine;
using UnityEngine.Networking;

namespace SplenSoft.Unity
{
    public class WebApiService : MonoProtectedSingletonR3<WebApiService>
    {
        [field: SerializeField]
        private InterfaceReference<IBaseUrlProvider> BaseUrlProvider { get; set; }

        /// <summary>
        /// Expects a fully-qualified URL path, no partial paths, handles retries 
        /// with exponential backoff in case of failed requests with response 
        /// code 0 which indicates a network error
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="queryParameters"></param>
        /// <returns></returns>
        public static async UniTask<UnityWebRequest> StandaloneGetRequest(
            UriBuilder builder, (string, string)[] queryParameters)
        {
            if (queryParameters.Length > 0)
            {
                var query = HttpUtility.ParseQueryString(builder.Query);
                foreach (var param in queryParameters)
                {
                    query[param.Item1] = param.Item2;
                }
                builder.Query = query.ToString();
            }

            string stringUri = builder.ToString();
            var request = UnityWebRequest.Get(stringUri);
            var operation = request.SendWebRequest();

            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            await WaitForOperation(operation);

            LogRequest(request);

            if (request.responseCode == 0)
            {
                await UniTask.Delay(_waitTime);
                _waitTime *= 2;
                return await StandaloneGetRequest(builder, queryParameters);
            }
            else
            {
                _waitTime = 2000;
            }

            return request;
        }

        public static async Task<UnityWebRequest> GetRequest(string endPoint,
            params (string, string)[] queryParameters)
        {
            UriBuilder builder = await GetUri(endPoint);
            return await StandaloneGetRequest(builder, queryParameters);
        }

        private static async Task<UriBuilder> GetUri(string endPoint)
        {
            var instance = await GetInstanceAsync();
            Uri uri = new Uri(await instance.BaseUrlProvider.Value.GetBaseUrlAsync());
            uri = new Uri(uri, endPoint);
            instance.Log($"Api request: {uri}", LogLevel.Verbose);
            return new UriBuilder(uri);
        }

        /// <summary>
        /// Handles escaped and unescaped JSON strings
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="jsonString"></param>
        /// <returns></returns>
        public static T Deserialize<T>(string jsonString)
        {
            bool isList = typeof(T).IsGenericType &&
                (typeof(T).GetGenericTypeDefinition() == typeof(List<>));

            if (isList && jsonString == "[]")
            {
                var genericList = Activator.CreateInstance(typeof(T));
                return (T)genericList;
            }

            try
            {
                string unescaped = JsonConvert.DeserializeObject<string>(
                    jsonString);

                return JsonConvert.DeserializeObject<T>(unescaped);
            }
            catch (JsonReaderException)
            {
                return JsonConvert.DeserializeObject<T>(jsonString);
            }
        }

        private static int _waitTime = 2000;

        public static async Task<UnityWebRequest> ApiPost(string endPoint, object postBody)
        {
            UriBuilder builder = await GetUri(endPoint);
            string stringUri = builder.ToString();
            using var textWriter = new StringWriter();
            var serializer = new JsonSerializer();
            serializer.Serialize(textWriter, postBody);
            string postData = textWriter.ToString();

            var request = new UnityWebRequest(stringUri, "POST")
            {
                downloadHandler = new DownloadHandlerBuffer()
            };

            if (!string.IsNullOrEmpty(postData))
            {
                byte[] array = Encoding.UTF8.GetBytes(postData);
                request.uploadHandler = new UploadHandlerRaw(array)
                {
                    contentType = "application/json"
                };
            }

            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            request.SetRequestHeader("Content-Type", "application/json");
            var operation = request.SendWebRequest();
            await WaitForOperation(operation);
            LogRequest(request);

            if (request.responseCode == 0)
            {
                await Task.Delay(_waitTime);
                _waitTime *= 2;
                return await ApiPost(endPoint, postBody);
            }
            else
            {
                _waitTime = 2000;
            }
            return request;
        }

        private static async void LogRequest(UnityWebRequest request)
        {
            var instance = await GetInstanceAsync();
            if (request != null && request.downloadHandler != null)
            {
                instance.Log($"Roberto API request to {request.uri}, status code {request.responseCode} returned: {request.downloadHandler.text}", LogLevel.Verbose);
            }
        }

        private static async UniTask WaitForOperation(UnityWebRequestAsyncOperation operation)
        {
            while (!operation.isDone)
            {
                await UniTask.Yield(Application.exitCancellationToken);
            }
        }

        //private static WWWForm GetFormFromArray((string, object)[] formFields)
        //{
        //    var form = formFields.Length > 0 ? new WWWForm() : null;
        //    if (form != null)
        //    {
        //        var logString = "Preparing WWWForm with fields: ";

        //        Array.ForEach(formFields, formField =>
        //        {
        //            var value = formField.Item2 != null ? formField.Item2.ToString() : "null";
        //            logString += formField.Item1 + ": " + value + ", ";
        //            form.AddField(formField.Item1, value);
        //        });

        //        Log(logString, LogLevel.Verbose);
        //    }
        //    return form;
        //}

        //private static WWWForm GetFormFromDictionary(Dictionary<string, string> formFields)
        //{
        //    if (formFields == null) return null;

        //    var form = formFields.Keys.Count > 0 ? new WWWForm() : null;
        //    if (form != null)
        //    {
        //        var logString = "Preparing WWWForm with fields: ";

        //        formFields.Keys.ToList().ForEach(key =>
        //        {
        //            var value = formFields[key] != null ? formFields[key].ToString() : "null";
        //            logString += key + ": " + formFields[key] + ", ";
        //            form.AddField(key, formFields[key]);
        //        });

        //        Log(logString, LogLevel.Verbose);
        //    }
        //    return form;
        //}

        //private static Dictionary<string, Sprite> _cachedSpritesFromWebURLs = new Dictionary<string, Sprite>();

        ///// <summary>
        ///// Expects a fully-qualified URL path, no partial paths, caches sprites already downloaded from the same path
        ///// </summary>
        //internal static async Task<Sprite> GetSpriteFromWebImage(string path)
        //{
        //    path = path.Replace("\"", "");

        //    if (_cachedSpritesFromWebURLs.ContainsKey(path))
        //    {
        //        return _cachedSpritesFromWebURLs[path];
        //    }

        //    Log($"Downloading texture from path {path}", LogLevel.Verbose);
        //    UnityWebRequest request = UnityWebRequestTexture.GetTexture(path);
        //    request.SendWebRequest();

        //    while (!request.isDone)
        //    {
        //        await Task.Yield();
        //        if (!Application.isPlaying)
        //        {
        //            throw new System.Exception("Application quit unexpectedly while downloading a texture");
        //        }
        //    }

        //    if (request.isHttpError)
        //    {
        //        return null;
        //    }

        //    request.disposeUploadHandlerOnDispose = true;
        //    request.disposeDownloadHandlerOnDispose = true;

        //    Log($"Successfully downloaded texture from path {path}", LogLevel.Verbose);

        //    Texture2D texture = DownloadHandlerTexture.GetContent(request);
        //    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0, 0));
        //    _cachedSpritesFromWebURLs[path] = sprite;
        //    return sprite;
        //}
    }
}