using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MagicLeap.ObjectAlignment
{
    public class InspectPanel : MonoBehaviour
    {
        public event Action OnDestroyed;

        [SerializeField]
        private Button _closeButton;

        [SerializeField]
        private Button _createOrEditPinsButton;

        [SerializeField]
        private TMP_Text _createOrEditPinsButtonText;

        [SerializeField]
        private Button _stopEditingPinsButton;

        [SerializeField]
        private Button _clearPinsButton;

        [SerializeField]
        private TMP_Text _pinsStatusText;

        [SerializeField]
        private Button _deleteButton;

        private External3DModel _external3DModel;

        public void Show(External3DModel external3DModel)
        {
            _external3DModel = external3DModel;
            _external3DModel.OnDestroyed += OnExternal3DModelDestroyed;
            _external3DModel.OnPinsChanged += PinsChanged;
            _external3DModel.OnEditingPinsChanged += ModelEditingPinsChanged;
        }

        private void ModelEditingPinsChanged(External3DModel _, bool editingPins)
        {
            UpdatePinsUI();
        }

        private void PinsChanged(External3DModel _)
        {
            UpdatePinsUI();
        }

        private void OnExternal3DModelDestroyed()
        {
            Hide();
        }

        public void Hide()
        {
            Destroy(gameObject);
        }

        public void OnDestroy()
        {
            _external3DModel.OnDestroyed -= OnExternal3DModelDestroyed;
            _external3DModel.OnPinsChanged -= PinsChanged;
            _external3DModel.OnEditingPinsChanged -= ModelEditingPinsChanged;
            OnDestroyed?.Invoke();
        }

        private void Start()
        {
            _closeButton.onClick.AddListener(OnCloseButtonClicked);
            _createOrEditPinsButton.onClick.AddListener(OnCreateOrEditPinsButtonClicked);
            _stopEditingPinsButton.onClick.AddListener(OnStopEditingPinsButtonClicked);
            _clearPinsButton.onClick.AddListener(OnClearPinsButtonClicked);
            _deleteButton.onClick.AddListener(OnDeleteButtonClicked);

            UpdatePinsUI();
        }

        private void UpdatePinsUI()
        {
            _createOrEditPinsButtonText.text =
                _external3DModel.Pins.Count == 0 ? "Create" : "Edit";
            _pinsStatusText.text = _external3DModel.Pins.Count != 1
                ? string.Format("{0} pins", _external3DModel.Pins.Count)
                : "1 pin";
            _clearPinsButton.gameObject.SetActive(
                _external3DModel.Pins.Count > 0);
            _createOrEditPinsButton.gameObject.SetActive(!_external3DModel.EditingPins);
            _stopEditingPinsButton.gameObject.SetActive(_external3DModel.EditingPins);
        }

        private void OnCloseButtonClicked()
        {
            Hide();
        }

        private void OnClearPinsButtonClicked()
        {
            _external3DModel.ClearPins();
        }

        private void OnCreateOrEditPinsButtonClicked()
        {
            _external3DModel.StartEditingPins();
        }

        private void OnStopEditingPinsButtonClicked()
        {
            _external3DModel.StopEditingPins();
        }

        private void OnDeleteButtonClicked()
        {
            _external3DModel.Delete();
        }
    }
}
