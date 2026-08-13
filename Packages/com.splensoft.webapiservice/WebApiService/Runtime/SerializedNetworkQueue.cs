using R3;
using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;

namespace SplenSoft.Unity
{
    [Serializable]
    public class SerializedNetworkQueue : MonoBehaviourR3
    {
        private static UnityEventR3<Exception> _onException = new();
        public static IDisposable OnException(Action<Exception> x) => _onException.Subscribe(x);

        [field: SerializeField]
        private WebApiService Service { get; set; }

        [field: SerializeField]
        private float QueueProcessIntervalSeconds { get; set; } = 1f;

        private float _queueProcessTimer;
        private bool _busy;

        private int _queueLength = -1;
        private string _lastError;

        private string FolderPath => Path.Combine(
            Application.persistentDataPath,
            "WebApiService",
            $"{Service.Name}");

        private void Update()
        {
            _queueProcessTimer += Time.deltaTime;
            if (_queueProcessTimer >= QueueProcessIntervalSeconds)
            {
                _queueProcessTimer = 0f;
                TryProcessQueue();
            }
        }

        /// <summary>
        /// Return the number of requests currently in the queue. Returns -1 if the queue has not been processed yet.
        /// </summary>
        public int GetQueueLength()
        {
            return _queueLength;
        }

        public string GetLastError()
        {
            return _lastError;
        }

        public async void EnqueuePostRequest(string endpoint, object postBody)
        {
            if (!Service.NameIsValid)
            {
                throw new Exception($"WebApiService Name '{Service.Name}' of object {Service.name} is invalid. Please remove any of the following characters: {new string(Path.GetInvalidFileNameChars())} and ensure the name is not empty or whitespace");
            }

            var guid = Guid.NewGuid().ToString();
            var ticks = DateTime.UtcNow.Ticks;
            string path = Path.Combine(FolderPath, $"{ticks}_{guid}");

            var postRequest = new SerializedRequest
            {
                Endpoint = endpoint,
                Body = postBody
            };

            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            string json = JsonConvert.SerializeObject(postRequest);
            //Debug.Log($"Enqueuing post request to {endpoint} with body: {json} at path: {path}");

            await File.WriteAllTextAsync(path, json, _cancellationDestroy.Token);
            Log($"Enqueued post request to {endpoint} at {path}", LogLevel.Verbose);
            TryProcessQueue();
        }

        public async void EnqueueGetRequest(string endpoint, params (string, string)[] queryParameters)
        {
            if (!Service.NameIsValid)
            {
                throw new Exception($"WebApiService Name '{Service.Name}' of object {Service.name} is invalid. Please remove any of the following characters: {new string(Path.GetInvalidFileNameChars())} and ensure the name is not empty or whitespace");
            }

            var guid = Guid.NewGuid().ToString();
            var ticks = DateTime.UtcNow.Ticks;
            string path = Path.Combine(FolderPath, $"{ticks}_{guid}");

            var getRequest = new SerializedRequest
            {
                Endpoint = endpoint
            };

            foreach (var (key, value) in queryParameters)
            {
                getRequest.QueryParameters.Add(new KeyValuePair<string, string>(key, value));
            }

            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }
            string json = JsonConvert.SerializeObject(getRequest);
            //Debug.Log($"Enqueuing get request to {endpoint} with query parameters: {json} at path: {path}");
            await File.WriteAllTextAsync(path, json, _cancellationDestroy.Token);
            Log($"Enqueued get request to {endpoint} at {path}", LogLevel.Verbose);
            TryProcessQueue();
        }

        private async void TryProcessQueue()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;

            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                // Get all files in folder
                Log($"Checking for queued requests in {FolderPath}", LogLevel.Verbose);
                var files = Directory.GetFiles(FolderPath);
                _queueLength = files.Length;
                if (files.Length == 0)
                {
                    Log("No queued requests found.", LogLevel.Verbose);
                    return;
                }

                // Process the oldest file
                DateTime oldestDateTime = DateTime.MaxValue;
                string oldestFile = null;
                Log("Queued requests found. Processing the oldest one.", LogLevel.Verbose);
                foreach (var file in files)
                {
                    Log($"Found queued request: {file}", LogLevel.Verbose);
                    var creationTime = File.GetCreationTime(file);
                    if (creationTime < oldestDateTime)
                    {
                        Log($"Found older queued request: {file}, {creationTime}", LogLevel.Verbose);
                        oldestDateTime = creationTime;
                        oldestFile = file;
                    }
                }

                // Deserialize the file into SerializedPostRequest
                Log($"Deserializing queued request: {oldestFile}", LogLevel.Verbose);
                var json = await File.ReadAllTextAsync(oldestFile, _cancellationDestroy.Token);

                // We can determine if it's a GET or POST request based on the presence of the "Body" property

                var request = JsonConvert.DeserializeObject<SerializedRequest>(json);

                // Send the request
                Log($"Sending queued request to {request.Endpoint}", LogLevel.Verbose);

                UnityWebRequest response;

                if (request.Body != null)
                {
                    response = await Service.PostRequest(request.Endpoint, request.Body);
                }
                else
                {
                    var queryParamsArray = request.QueryParameters
                        .Select(kvp => (kvp.Key, kvp.Value))
                        .ToArray();
                    
                    response = await Service.GetRequest(
                        request.Endpoint, 
                        queryParamsArray);
                }

                try
                {
                    bool isSuccess = response.responseCode >= 200 && response.responseCode < 300;
                    bool canNeverWork = response.responseCode >= 300 && response.responseCode < 500;
                    bool isServerError = response.responseCode >= 500 && response.responseCode < 600;

                    // If successful
                    if (isSuccess)
                    {
                        File.Delete(oldestFile);
                        Log($"Successfully processed queued request: {oldestFile}", LogLevel.Verbose);
                    }
                    else if (canNeverWork)
                    {
                        // If it can never work, delete the file to prevent retrying
                        File.Delete(oldestFile);

                        _lastError = $"Request to {request.Endpoint} failed with response code {response.responseCode}. The request will be discarded.";

                        Debug.LogError(
                            $"Request to {request.Endpoint} failed with response code {response.responseCode}. The request will be discarded.");
                    }
                    else if (isServerError)
                    {
                        _lastError = $"Request to {request.Endpoint} failed with response code {response.responseCode}. The request will be retried on the next attempt.";

                        Debug.LogWarning(
                            $"Request to {request.Endpoint} failed with response code {response.responseCode}. The request will be retried on the next attempt.");
                    }

                    // Every other response code (like 0 for network error) will be retried on the next attempt
                    Log($"Finished processing queued request: {oldestFile}. Success: {isSuccess}, CanNeverWork: {canNeverWork}", LogLevel.Verbose);
                }
                finally
                {
                    response.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing network queue: {ex.Message}");
                Debug.LogException(ex);
                _onException?.Invoke(ex);
            }
            finally
            {
                _busy = false;
            }
        }
    }

    [Serializable]
    internal class SerializedRequest
    {
        public string Endpoint { get; set; }
        public object Body { get; set; }
        public List<KeyValuePair<string, string>> QueryParameters { get; set; } = new();
    }
}