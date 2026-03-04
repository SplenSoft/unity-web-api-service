using Cysharp.Threading.Tasks;

namespace SplenSoft.Unity
{
    public interface IBaseUrlProvider
    {
        public abstract UniTask<string> GetBaseUrlAsync();
    }
}