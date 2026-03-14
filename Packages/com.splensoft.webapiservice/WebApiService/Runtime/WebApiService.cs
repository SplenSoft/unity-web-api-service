using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using UnityEngine;
using UnityEngine.Networking;

namespace SplenSoft.Unity
{
    public class WebApiService : MonoBehaviourR3
    {
        private static int _waitTime = 2000;

        [field: SerializeField, Tooltip("Must be unique")]
        public string Name { get; private set; }

        [field: SerializeField]
        private InterfaceReference<IBaseUrlProvider> BaseUrlProvider { get; set; }

        [field: SerializeField]
        public SerializedNetworkQueue NetworkQueue { get; private set; }

        [field: SerializeField]
        private bool SimulateNoInternet { get; set; }

        public bool NameIsValid => !string.IsNullOrWhiteSpace(Name) && IsValidFileNameRegex(Name);

        [field: SerializeField]
        private List<string> ActiveRequests { get; set; } = new();


        protected override void Awake()
        {
            base.Awake();

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new Exception("WebApiService Name cannot be null or whitespace.");
            }

            if (!IsValidFileNameRegex(Name))
            {
                throw new Exception($"WebApiService Name '{Name}' contains invalid characters. Please remove any of the following characters: {new string(Path.GetInvalidFileNameChars())}");
            }

            ActiveRequests.Clear();
        }

        public static bool IsValidFileNameRegex(string fileName)
        {
            string pattern = @"[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]";
            return !Regex.IsMatch(fileName, pattern);
        }

        /// <summary>
        /// Creates a simulated network error response with status code 0
        /// </summary>
        private UnityWebRequest CreateSimulatedNetworkErrorResponse(string url)
        {
            Log($"Simulating no internet connection for request to {url}", LogLevel.Verbose);
            var request = new UnityWebRequest(url, "GET")
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            // UnityWebRequest with responseCode 0 simulates a network error
            return request;
        }

        /// <summary>
        /// Expects a fully-qualified URL path, no partial paths, handles retries 
        /// with exponential backoff in case of failed requests with response 
        /// code 0 which indicates a network error
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="queryParameters"></param>
        /// <returns></returns>
        public async UniTask<UnityWebRequest> StandaloneGetRequest(
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

            if (SimulateNoInternet)
            {
                return CreateSimulatedNetworkErrorResponse(stringUri);
            }

            var request = UnityWebRequest.Get(stringUri);
            var operation = request.SendWebRequest();

            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            await WaitForOperation(operation);

            LogRequest(request);

            //if (request.responseCode == 0)
            //{
            //    await UniTask.Delay(_waitTime);
            //    _waitTime *= 2;
            //    return await StandaloneGetRequest(builder, queryParameters);
            //}
            //else
            //{
            //    _waitTime = 2000;
            //}

            return request;
        }

        public async UniTask<UnityWebRequest> GetRequest(string endPoint,
            params (string, string)[] queryParameters)
        {
            ActiveRequests.Add(endPoint);
            try
            {
                UriBuilder builder = await GetUri(endPoint);
                return await StandaloneGetRequest(builder, queryParameters);
            }
            finally
            {
                ActiveRequests.Remove(endPoint);
            }
        }

        private async UniTask<UriBuilder> GetUri(string endPoint)
        {
            Uri uri = new Uri(await BaseUrlProvider.Value.GetBaseUrlAsync());
            uri = new Uri(uri, endPoint);
            Log($"Api request: {uri}", LogLevel.Verbose);
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

        public async UniTask<UnityWebRequest> ApiPost(string endPoint, object postBody)
        {
            ActiveRequests.Add(endPoint);
            try
            {
                UriBuilder builder = await GetUri(endPoint);
                string stringUri = builder.ToString();

                if (SimulateNoInternet)
                {
                    return CreateSimulatedNetworkErrorResponse(stringUri);
                }

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

                //if (request.responseCode == 0)
                //{
                //    await Task.Delay(_waitTime);
                //    _waitTime *= 2;
                //    return await ApiPost(endPoint, postBody);
                //}
                //else
                //{
                //    _waitTime = 2000;
                //}
                return request;
            }
            finally
            {
                ActiveRequests.Remove(endPoint);
            }
        }

        private async void LogRequest(UnityWebRequest request)
        {
            if (request != null && request.downloadHandler != null)
            {
                Log($"Roberto API request to {request.uri}, status code {request.responseCode} returned: {request.downloadHandler.text}", LogLevel.Verbose);
            }
        }

        private static async UniTask WaitForOperation(UnityWebRequestAsyncOperation operation)
        {
            while (!operation.isDone)
            {
                await UniTask.Yield(Application.exitCancellationToken);
            }
        }
    }
}