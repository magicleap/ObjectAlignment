using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// A 3D model imported into the scene.
    /// </summary>
    public class External3DModel : MonoBehaviour
    {
        public class PinData
        {
            public Vector3 Position;
            public Vector3 ModelPosition;
        }

        [HideInInspector]
        public string Id;

        [HideInInspector]
        public string AnchorId;

        public Transform ModelParentTransform;

        [SerializeField]
        private TMP_Text _statusText;

        [SerializeField]
        private GameObject _loadingCube;

        [SerializeField]
        private float _rotationSpeed = 75;

        [SerializeField]
        private GameObject _pinFromPrefab;

        [SerializeField]
        private GameObject _pinToPrefab;

        public event Action<External3DModel, XRRayInteractor> OnSelect;
        public event Action<External3DModel> OnFirstHoverEntered;
        public event Action<External3DModel> OnLastHoverExited;
        public event Action<External3DModel> OnPinsChanged;
        public event Action<External3DModel, bool> OnEditingPinsChanged;

        public string FileName => _fileName;

        public List<PinData> Pins => _pins;

        public InspectPanel InspectPanel
        {
            get => _inspectPanel;
            set
            {
                _inspectPanel = value;
                _inspectPanel.OnDestroyed += OnInspectPanelDestroyed;
            }
        }

        private void OnInspectPanelDestroyed()
        {
            _inspectPanel = null;
            StopEditingPins();
        }

        public event Action OnDestroyed;

        private string _fileName;
        private Quaternion _loadingCubeInitialRotation;
        private XRGrabInteractable _grabInteractable;
        private XRSimpleInteractable _simpleInteractable;
        private bool _loaded;
        private bool _failed;
        private IEnumerator _delayLoadingCubeCoroutine;
        private bool _started;
        private bool _usingLaserTool;
        private InspectPanel _inspectPanel;
        private Vector3 _pinStartPosition;
        private Transform _pinTransform;
        private bool _editingPins;
        private Camera _camera;
        private List<PinData> _pins = new();

        private List<Pin> _fromPins = new();
        private List<Pin> _toPins = new();

        private const float MinScale = 0.01f;
        private const float MaxScale = 1.0f;

        private void Awake()
        {
            _camera = Camera.main;
        }

        public void Start()
        {
            _loadingCubeInitialRotation = _loadingCube.transform.rotation;

            _loadingCube.SetActive(false);
            _delayLoadingCubeCoroutine = ShowLoadingCubeWithDelayCoroutine();
            StartCoroutine(_delayLoadingCubeCoroutine);

            _started = true;
            if (_loaded)
            {
                OnLoadedAndStarted();
            }
        }

        public bool EditingPins => _editingPins;

        private void OnInteractableSelectExited(SelectExitEventArgs selectExitEventArgs)
        {
            OnSelect?.Invoke(this, selectExitEventArgs.interactorObject as XRRayInteractor);
        }

        private void OnInteractableFirstHoverEntered(HoverEnterEventArgs arg0)
        {
            OnFirstHoverEntered?.Invoke(this);
        }

        private void OnInteractableLastHoverExited(HoverExitEventArgs arg0)
        {
            OnLastHoverExited?.Invoke(this);
        }

        public void Update()
        {
            if (_loadingCube.activeSelf && !_failed)
            {
                _loadingCube.transform.rotation =
                    Quaternion.AngleAxis(Time.timeSinceLevelLoad * _rotationSpeed, Vector3.up)
                    * _loadingCubeInitialRotation;
            }

            if (_pinTransform != null)
            {
                UpdateCurrentPin();
            }

            UpdateCameraBasedPinOffset();
            UpdatePinVisuals();
        }

        private void UpdateCameraBasedPinOffset()
        {
            if (_pins.Count > 0)
            {
                Vector3[] toPinPositions = new Vector3[_pins.Count];
                float[] distanceToPins = new float[_pins.Count];
                float maxDistanceToPins = 0;

                for (int i = 0; i < _pins.Count; i++)
                {
                    PinData pinData = _pins[i];
                    Vector3 toPinPosition = transform.parent.TransformPoint(pinData.Position);
                    toPinPositions[i] = toPinPosition;
                    float distanceToPin = (_camera.transform.position - toPinPosition).magnitude;
                    distanceToPins[i] = distanceToPin;
                    maxDistanceToPins = Mathf.Max(maxDistanceToPins, distanceToPin);
                }

                maxDistanceToPins = Math.Min(maxDistanceToPins, 5);

                Vector3 cameraBasedOffset = Vector3.zero;
                for (int i = 0; i < _pins.Count; i++)
                {
                    PinData pinData = _pins[i];

                    float weight = Mathf.Clamp01(1 - distanceToPins[i] / maxDistanceToPins);
                    if (weight > 0)
                    {
                        Vector3 fromPinPosition = transform.TransformPoint(pinData.ModelPosition);

                        cameraBasedOffset += (toPinPositions[i] - fromPinPosition) * weight;
                    }
                }

                ModelParentTransform.position = transform.position + cameraBasedOffset;
            }
            else
            {
                ModelParentTransform.localPosition = Vector3.zero;
            }
        }

        public void Initialize(string fileName)
        {
            _fileName = fileName;
            _statusText.text = string.Format("Loading {0}...", fileName);
            _loadingCube.GetComponent<Renderer>().material.color = new Color(0, 0.7f, 0);
        }

        public void OnDestroy()
        {
            foreach (Pin fromPin in _fromPins)
            {
                Destroy(fromPin.gameObject);
            }
            foreach (Pin toPin in _toPins)
            {
                Destroy(toPin.gameObject);
            }
            _fromPins.Clear();
            _toPins.Clear();
            OnDestroyed?.Invoke();
        }

        private IEnumerator ShowLoadingCubeWithDelayCoroutine()
        {
            yield return new WaitForSeconds(0.5f);

            if (!_loaded)
            {
                _loadingCube.SetActive(true);
            }
        }

        public void OnLoadCompleted()
        {
            if (_delayLoadingCubeCoroutine != null)
            {
                StopCoroutine(_delayLoadingCubeCoroutine);
                _delayLoadingCubeCoroutine = null;
            }

            _loaded = true;
            if (_started)
            {
                OnLoadedAndStarted();
            }
        }

        private void OnLoadedAndStarted()
        {
            List<Collider> m_Colliders = new List<Collider>();
            GetComponentsInChildren(m_Colliders);

            _loadingCube.SetActive(false);
            _statusText.gameObject.SetActive(false);

            UpdateGrabbable();
        }

        public void OnLoadFailed(bool notFound)
        {
            _failed = true;
            if (notFound)
            {
                _statusText.text = string.Format("Model {0} not found", _fileName);
                _loadingCube.GetComponent<Renderer>().material.color = new Color(0.75f, 0.4f, 0);
            }
            else
            {
                _statusText.text = string.Format("Failed to load {0}", _fileName);
                _loadingCube.GetComponent<Renderer>().material.color = new Color(0.75f, 0, 0);
            }
        }

        public void SetUsingLaserTool(bool usingLaserTool)
        {
            _usingLaserTool = usingLaserTool;
            UpdateGrabbable();
        }

        private void UpdateGrabbable()
        {
            bool enableGrab = _usingLaserTool && _loaded && _started && _pins.Count == 0;

            if (!enableGrab && _grabInteractable != null)
            {
                Destroy(_grabInteractable);
                _grabInteractable = null;
            }

            if (enableGrab && _simpleInteractable != null)
            {
                Destroy(_simpleInteractable);
                _simpleInteractable = null;
            }

            if (enableGrab && _grabInteractable == null)
            {
                _grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
                _grabInteractable.selectExited.AddListener(OnInteractableSelectExited);
                _grabInteractable.firstHoverEntered.AddListener(OnInteractableFirstHoverEntered);
                _grabInteractable.lastHoverExited.AddListener(OnInteractableLastHoverExited);
                _grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                _grabInteractable.useDynamicAttach = true;
                _grabInteractable.throwOnDetach = false;
            }

            if (!enableGrab && _simpleInteractable == null)
            {
                _simpleInteractable = gameObject.AddComponent<XRSimpleInteractable>();
                _simpleInteractable.enabled = true;
                _simpleInteractable.selectExited.AddListener(OnInteractableSelectExited);
                _simpleInteractable.firstHoverEntered.AddListener(OnInteractableFirstHoverEntered);
                _simpleInteractable.lastHoverExited.AddListener(OnInteractableLastHoverExited);
            }
        }

        public void Scale(float scaleValue)
        {
            scaleValue = Mathf.Clamp(scaleValue, MinScale, MaxScale);
            transform.localScale = Vector3.one * scaleValue;
        }

        public void ClearPins()
        {
            if (_pins.Count > 0)
            {
                _pins.Clear();
                OnPinsChanged?.Invoke(this);
                UpdateTransformForPinChanges();
            }
        }

        public void StartEditingPins()
        {
            _editingPins = true;
            OnEditingPinsChanged?.Invoke(this, true);
        }

        public void StopEditingPins()
        {
            if (_editingPins)
            {
                CancelPinning();

                _editingPins = false;
                OnEditingPinsChanged?.Invoke(this, false);
            }
        }

        public void Delete()
        {
            Destroy(gameObject);
        }

        private void UpdateCurrentPin()
        {
            if (_pins.Count > 0)
            {
                PinData currentPin = _pins.Last();
                currentPin.Position = transform.parent.InverseTransformPoint(_pinTransform.position);

                OnPinsChanged?.Invoke(this);
                UpdateTransformForPinChanges();
            }
        }

        private void UpdatePinVisuals()
        {
            while (_editingPins && _fromPins.Count < _pins.Count)
            {
                Pin fromPin = Instantiate(_pinFromPrefab, transform.parent).GetComponent<Pin>();
                _fromPins.Add(fromPin);
                Pin toPin = Instantiate(_pinToPrefab, transform.parent).GetComponent<Pin>();
                _toPins.Add(toPin);

                fromPin.AttachToOtherPin(toPin);
                fromPin.OnSelect += () => OnPinSelected(fromPin);
                toPin.OnSelect += () => OnPinSelected(toPin);
            }

            while ((!_editingPins && _fromPins.Count > 0)
                   || _fromPins.Count > _pins.Count)
            {
                Destroy(_fromPins.Last().gameObject);
                Destroy(_toPins.Last().gameObject);
                _fromPins.RemoveAt(_fromPins.Count - 1);
                _toPins.RemoveAt(_toPins.Count - 1);
            }

            if (_editingPins)
            {
                for (int i = 0; i < _pins.Count; i++)
                {
                    PinData pinData = _pins[i];
                    Pin fromPin = _fromPins[i];
                    Pin toPin = _toPins[i];
                    fromPin.transform.position = ModelParentTransform.TransformPoint(
                        pinData.ModelPosition);
                    toPin.transform.localPosition = pinData.Position;
                    fromPin.SetDeletable(_pinTransform == null);
                    toPin.SetDeletable(_pinTransform == null);
                }
            }
        }

        private void OnPinSelected(Pin pin)
        {
            for (int i = 0; i < _fromPins.Count; i++)
            {
                if (ReferenceEquals(pin, _fromPins[i])
                    || ReferenceEquals(pin, _toPins[i]))
                {
                    _pins.RemoveAt(i);
                    OnPinsChanged?.Invoke(this);
                    UpdateTransformForPinChanges();
                    break;
                }
            }
        }

        private void UpdateTransformForPinChanges()
        {
            if (_pins.Count >= 2)
            {
                List<Vector3> fromPoints = new();
                List<Vector3> toPoints = new();
                foreach (PinData pin in _pins)
                {
                    fromPoints.Add(pin.ModelPosition);
                    toPoints.Add(pin.Position);
                }

                if (_pins.Count == 2)
                {
                    Vector3 fromAdjust = Vector3.Cross(
                        (fromPoints.Last() - fromPoints.First()).normalized, Vector3.up).normalized * .01f;
                    Vector3 toAdjust = Vector3.Cross(
                        (toPoints.Last() - toPoints.First()).normalized, Vector3.up).normalized * .01f;
                    fromPoints.Add((fromPoints.Last() + fromPoints.First()) * 0.5f + fromAdjust);
                    toPoints.Add((toPoints.Last() + toPoints.First()) * 0.5f + toAdjust);
                }

                Matrix4x4 transform4x4 = Transform3DBestFit.Solve(fromPoints, toPoints);
                transform.localPosition = transform4x4.GetPosition();
                transform.localRotation = transform4x4.rotation;
            }
            else if (_pins.Count == 1)
            {
                Vector3 modelPosition = _pins.Last().ModelPosition;
                Vector3 position = _pins.Last().Position;

                Vector3 fromWorldPosition = transform.TransformPoint(modelPosition);
                Vector3 fromPosition = transform.parent.InverseTransformPoint(fromWorldPosition);

                transform.localPosition += position - fromPosition;
            }
        }

        public void StartPinning(Transform pinTransform)
        {
            _pinStartPosition = pinTransform.position;
            _pinTransform = pinTransform;

            _pins.Add(new PinData
            {
                ModelPosition = ModelParentTransform.InverseTransformPoint(_pinStartPosition),
                Position = transform.parent.InverseTransformPoint(_pinStartPosition)
            });

            OnPinsChanged?.Invoke(this);
            UpdateGrabbable();
        }

        public void FinishPinning()
        {
            _pinTransform = null;
        }

        public void CancelPinning()
        {
            if (_pinTransform == null)
            {
                return;
            }

            _pinTransform = null;
            if (_pins.Count > 0)
            {
                _pins.RemoveAt(_pins.Count - 1);
                OnPinsChanged?.Invoke(this);
                UpdateTransformForPinChanges();
            }
        }
    }
}