using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace MagicLeap.ObjectAlignment
{
    public class MainPanel : MonoBehaviour
    {
        public event Action<External3DModelManager.ModelInfo> OnLoad3DModel;

        [SerializeField]
        private External3DModelManager _external3dModelManager;

        [SerializeField]
        private GameObject _external3dModelListEntryPrefab;

        [SerializeField]
        private Transform _exteral3dModelListContainer;

        [SerializeField]
        private TMP_Text _directoryPathText;

        private IEnumerator _load3DModelListPeriodicallyCoroutine;

        // Start is called before the first frame update
        void Start()
        {
            _directoryPathText.text = string.Format("(Place glb/gltf files in {0})",
                Application.persistentDataPath);

            _external3dModelManager.OnModelsListUpdated += OnModelListUpdated;
        }

        private void OnEnable()
        {
            _load3DModelListPeriodicallyCoroutine = Load3DModelListPeriodicallyCoroutine();
            StartCoroutine(_load3DModelListPeriodicallyCoroutine);
        }

        private void OnDisable()
        {
            StopCoroutine(_load3DModelListPeriodicallyCoroutine);
            _load3DModelListPeriodicallyCoroutine = null;
        }

        private void OnModelListUpdated()
        {
            var existingEntries = _exteral3dModelListContainer.transform
                .GetComponentsInChildren<External3DModelListEntry>();

            for (int i = _external3dModelManager.Models.Length; i < existingEntries.Length; i++)
            {
                Destroy(existingEntries[i].gameObject);
            }

            for (int i = 0; i < _external3dModelManager.Models.Length; i++)
            {
                External3DModelManager.ModelInfo modelInfo = _external3dModelManager.Models[i];
                External3DModelListEntry entry;
                if (i < existingEntries.Length)
                {
                    entry = existingEntries[i];
                }
                else
                {
                    entry = Instantiate(
                            _external3dModelListEntryPrefab, _exteral3dModelListContainer)
                        .GetComponent<External3DModelListEntry>();
                }
                entry.SetData(modelInfo, () => OnLoad3DModel?.Invoke(modelInfo));
            }
        }

        private void OnDestroy()
        {
            _external3dModelManager.OnModelsListUpdated -= OnModelListUpdated;
        }

        private IEnumerator Load3DModelListPeriodicallyCoroutine()
        {
            while (true)
            {
                _external3dModelManager.RefreshModelList();

                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}