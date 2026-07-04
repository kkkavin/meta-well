using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Actions
{
    [AddComponentMenu("Convai/Actions/Unity Event Action Executor")]
    public sealed class UnityEventActionExecutor : MonoBehaviour, IConvaiActionExecutor
    {
        [SerializeField] private UnityEvent _onExecute;

        public Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            _onExecute?.Invoke();
            return Task.FromResult(ConvaiActionExecutionResult.Succeeded());
        }
    }
}
