using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Actions;
using UnityEngine;
using UnityEngine.AI;

namespace Convai.Sample.Behaviors
{
    /// <summary>
    ///     Sample executor that moves a NavMeshAgent to the resolved action target.
    /// </summary>
    [AddComponentMenu("Convai/Samples/NavMesh Move To Action Executor")]
    public sealed class NavMeshMoveToActionExecutor : MonoBehaviour, IConvaiActionExecutor
    {
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private float _stoppingDistance = 0.5f;

        public async Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetGo = invocation.ResolvedTarget?.GameObjectReference;
            if (targetGo == null)
                return ConvaiActionExecutionResult.Failed("No target resolved");

            NavMeshAgent agent = _agent != null ? _agent : GetComponent<NavMeshAgent>();
            if (agent == null)
                return ConvaiActionExecutionResult.Failed("NavMeshAgent not found");

            agent.stoppingDistance = _stoppingDistance;
            agent.SetDestination(targetGo.transform.position);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!agent.pathPending && agent.remainingDistance <= _stoppingDistance)
                    break;

                await Task.Yield();
            }

            return ConvaiActionExecutionResult.Succeeded();
        }

        private void Awake()
        {
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();
        }
    }
}
