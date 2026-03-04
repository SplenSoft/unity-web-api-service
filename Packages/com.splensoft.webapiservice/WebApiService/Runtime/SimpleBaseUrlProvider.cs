using Cysharp.Threading.Tasks;
using SplenSoft.Unity;
using System;
using UnityEngine;

namespace SplenSoft.Unity
{
    [Serializable]
    public class SimpleBaseUrlProvider : IBaseUrlProvider
    {
        [field: SerializeField]
        private string Url { get; set; }

        public async UniTask<string> GetBaseUrlAsync()
        {
            return await UniTask.FromResult(Url);
        }
    }
}