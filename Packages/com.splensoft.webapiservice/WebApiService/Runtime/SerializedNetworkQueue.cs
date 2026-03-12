using R3;
using System;
using System.IO;
using UnityEngine;

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

        public async void EnqueuePostRequest(string endpoint, object postBody)
        {
            if (!Service.NameIsValid)
            {
                throw new Exception($"WebApiService Name '{Service.Name}' of object {Service.name} is invalid. Please remove any of the following characters: {new string(Path.GetInvalidFileNameChars())} and ensure the name is not empty or whitespace");
            }

            var guid = Guid.NewGuid().ToString();
            var ticks = DateTime.UtcNow.Ticks;
            string path = Path.Combine(FolderPath, $"{ticks}_{guid}");

            var postRequest = new SerializedPostRequest
            {
                Endpoint = endpoint,
                Body = postBody
            };

            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            await File.WriteAllTextAsync(path, JsonUtility.ToJson(postRequest), _cancellationDestroy.Token);
            Log($"Enqueued post request to {endpoint} at {path}", LogLevel.Verbose);
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
                var json = File.ReadAllText(oldestFile);
                var postRequest = JsonUtility.FromJson<SerializedPostRequest>(json);

                // Send the request
                Log($"Sending queued request to {postRequest.Endpoint}", LogLevel.Verbose);
                var response = await Service.ApiPost(postRequest.Endpoint, postRequest.Body);

                bool isSuccess = response.responseCode >= 200 && response.responseCode < 300;
                bool canNeverWork = response.responseCode >= 300 && response.responseCode < 500;

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

                    Debug.LogError(
                        $"Request to {postRequest.Endpoint} failed with response code {response.responseCode}. The request will be discarded.");
                }

                // Every other response code (like 0 for network error) will be retried on the next attempt
                Log($"Finished processing queued request: {oldestFile}. Success: {isSuccess}, CanNeverWork: {canNeverWork}", LogLevel.Verbose);
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
    internal class SerializedPostRequest
    {
        public string Endpoint { get; set; }
        public object Body { get; set; }
    }
}