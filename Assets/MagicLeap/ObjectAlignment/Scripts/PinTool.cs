using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// The object pinning tool.
    /// </summary>
    public class PinTool : MonoBehaviour
    {
        public event Action OnPinCancelled;
        public event Action OnPinFinished;

        [SerializeField]
        private XRBaseInteractable _interactable;

        [SerializeField]
        private GameObject _pinFrom;

        [SerializeField]
        private GameObject _interactableBackPlate;

        [SerializeField]
        private XRRayInteractor _interactor;

        private bool _pinning;
        private int _3dModelTargettedCount;

        private void OnDisable()
        {
            OnPinCancelled?.Invoke();
        }

        private void Awake()
        {
            _interactableBackPlate.gameObject.SetActive(false);
            _interactor.hoverEntered.AddListener(OnInteractorHoverEntered);
            _interactor.hoverExited.AddListener(OnInteractorHoverExited);
        }

        private void OnInteractorHoverEntered(HoverEnterEventArgs hoverEnterEventArgs)
        {
            if (hoverEnterEventArgs.interactableObject != null
                && hoverEnterEventArgs.interactableObject.transform.TryGetComponent(
                    out External3DModel _))
            {
                _3dModelTargettedCount++;
            }

            UpdatePinFromVisibility();
        }

        private void OnInteractorHoverExited(HoverExitEventArgs hoverExitEventArgs)
        {
            if (hoverExitEventArgs.interactableObject != null
                && hoverExitEventArgs.interactableObject.transform.TryGetComponent(
                    out External3DModel _))
            {
                _3dModelTargettedCount--;
            }

            UpdatePinFromVisibility();
        }

        private void Start()
        {
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        private void Update()
        {
            if (!_pinning)
            {
                if (_interactor.TryGetCurrent3DRaycastHit(out RaycastHit raycastHit))
                {
                    transform.position = raycastHit.point;
                }
            }
        }

        private void OnSelectExited(SelectExitEventArgs selectExitEventArgs)
        {
            if (_pinning)
            {
                OnPinFinished?.Invoke();
            }
        }

        public void StartPinning()
        {
            SetIsPinning(true);
        }

        public void FinishPinning()
        {
            SetIsPinning(false);
        }

        public void CancelPinning()
        {
            SetIsPinning(false);
        }

        private void SetIsPinning(bool pinning)
        {
            _pinning = pinning;
            _interactableBackPlate.SetActive(_pinning);

            UpdatePinFromVisibility();
        }

        private void UpdatePinFromVisibility()
        {
            _pinFrom.gameObject.SetActive(!_pinning && _3dModelTargettedCount > 0);
        }
    }
}