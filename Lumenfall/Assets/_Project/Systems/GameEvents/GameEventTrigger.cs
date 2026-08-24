using UnityEngine;
using UnityEngine.Events;

namespace Lumenfall.GameEvents
{
    public enum GameEventTriggerMoment
    {
        Manual,
        OnEnable,
        Start,
        OnTriggerEnter3D,
        OnTriggerEnter2D
    }

    /// <summary>
    /// Scene-facing trigger for a global event. NPC interaction scripts can also leave this in
    /// Manual mode and call Trigger from their interaction callback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameEventTrigger : MonoBehaviour
    {
        [SerializeField] private string eventId;
        [SerializeField] private GameEventTriggerMoment triggerMoment = GameEventTriggerMoment.Manual;
        [SerializeField] private bool triggerOnlyOnce = true;
        [SerializeField] private bool requireTag;
        [SerializeField] private string requiredTag = "Player";
        [SerializeField] private UnityEvent onTriggered;
        [SerializeField] private UnityEvent onRejected;

        private bool hasTriggered;

        public string EventId
        {
            get => eventId;
            set => eventId = value;
        }

        public GameEventTriggerResult Trigger()
        {
            if (triggerOnlyOnce && hasTriggered)
            {
                return new GameEventTriggerResult(
                    GameEventTriggerStatus.AlreadyTriggered,
                    null,
                    $"Scene trigger '{name}' has already succeeded.");
            }

            GameEventTriggerResult result = GameEventManager.Instance.TriggerEvent(eventId);
            if (result.Succeeded)
            {
                hasTriggered = true;
                onTriggered?.Invoke();
            }
            else
            {
                onRejected?.Invoke();
            }

            return result;
        }

        public void ResetLocalTrigger()
        {
            hasTriggered = false;
        }

        private void OnEnable()
        {
            if (triggerMoment == GameEventTriggerMoment.OnEnable)
            {
                Trigger();
            }
        }

        private void Start()
        {
            if (triggerMoment == GameEventTriggerMoment.Start)
            {
                Trigger();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (triggerMoment == GameEventTriggerMoment.OnTriggerEnter3D && MatchesTag(other.gameObject))
            {
                Trigger();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (triggerMoment == GameEventTriggerMoment.OnTriggerEnter2D && MatchesTag(other.gameObject))
            {
                Trigger();
            }
        }

        private bool MatchesTag(GameObject candidate)
        {
            return !requireTag || candidate.CompareTag(requiredTag);
        }
    }
}
