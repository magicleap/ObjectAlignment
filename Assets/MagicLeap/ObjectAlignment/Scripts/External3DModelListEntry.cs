using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicLeap.ObjectAlignment
{
    public class External3DModelListEntry : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _fileNameText;

        [SerializeField]
        private Button _openFileButton;

        private Action _onOpenFile;

        private void Awake()
        {
            _openFileButton.onClick.AddListener(OnClickOpenFileButton);
        }

        private void OnClickOpenFileButton()
        {
            _onOpenFile?.Invoke();
        }

        public void SetData(External3DModelManager.ModelInfo modelInfo, Action onOpenFile)
        {
            _fileNameText.text = modelInfo.FileName;
            _onOpenFile = onOpenFile;
        }
    }
}