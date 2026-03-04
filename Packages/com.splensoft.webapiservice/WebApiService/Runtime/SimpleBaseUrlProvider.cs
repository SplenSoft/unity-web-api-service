using Cysharp.Threading.Tasks;
using SplenSoft.Unity;
using System;
using UnityEngine;

namespace SplenSoft.Unity
{
    [CreateAssetMenu(
        fileName = "SimpleBaseUrlProvider", 
        menuName = "Scriptable Objects/SplenSoft/Web Api Service/Simple Base Url Provider")]
    public class SimpleBaseUrlProvider : ScriptableObject, IBaseUrlProvider
    {
        [field: SerializeField]
        private string Url { get; set; }

        public async UniTask<string> GetBaseUrlAsync()
        {
            return await UniTask.FromResult(Url);
        }
    }
}