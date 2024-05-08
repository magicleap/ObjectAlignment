using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit;

namespace MagicLeap.ObjectAlignment
{
    public class Pin : MonoBehaviour
    {
        public event Action OnSelect;

        [SerializeField]
        private Transform _rotationTransform;

        [SerializeField]
        private bool _pinFrom;

        [SerializeField]
        private LineRenderer _connectorLine;

        [SerializeField]
        private XRBaseInteractable _interactable;

        [SerializeField]
        private Renderer _pinHeadRenderer;

        [SerializeField]
        private Material _normalPinMaterial;

        [SerializeField]
        private Material _deletePinMaterial;

        [SerializeField]
        private Collider _headCollider;

        [SerializeField]
        private Quaternion _pinLocalRotation;

        private Camera _camera;
        private Pin _otherPin;
        private bool _deletable;

        // Start is called before the first frame update
        void Start()
        {
            _camera = Camera.main;
            _interactable.selectExited.AddListener(OnInteractableSelectExited);
            _interactable.firstHoverEntered.AddListener(OnInteractableFirstHoverEntered);
            _interactable.lastHoverExited.AddListener(OnInteractableLastHoverExited);

            UpdateDeletableUI();
        }

        private void OnInteractableFirstHoverEntered(HoverEnterEventArgs arg0)
        {
            UpdateDeletableUI();
        }

        private void OnInteractableLastHoverExited(HoverExitEventArgs arg0)
        {
            UpdateDeletableUI();
        }

        private void OnInteractableSelectExited(SelectExitEventArgs selectExitEventArgs)
        {
            OnSelect?.Invoke();
        }

        // Update is called once per frame
        void Update()
        {
            Vector3 cameraToPin = transform.position - _camera.transform.position;
            _rotationTransform.LookAt(_rotationTransform.position + cameraToPin, Vector3.up);
            _rotationTransform.rotation *= _pinLocalRotation;

            if (_otherPin != null)
            {
                _connectorLine.SetPosition(0, transform.position);
                _connectorLine.SetPosition(1, _otherPin.transform.position);
            }
        }

        public void AttachToOtherPin(Pin otherPin)
        {
            _otherPin = otherPin;
            _connectorLine.gameObject.SetActive(true);
        }

        public void SetDeletable(bool deletable)
        {
            _deletable = deletable;
            UpdateDeletableUI();
        }

        private void UpdateDeletableUI()
        {
            _headCollider.enabled = _deletable;
            _pinHeadRenderer.material = _interactable.isHovered && _deletable ?
                _deletePinMaterial : _normalPinMaterial;
        }
    }
}