using UnityEngine;

namespace MagicLeap.ObjectAlignment
{
    public class Billboard : MonoBehaviour {

        private void Update()
        {
            Vector3 cameraToPosition =
                (transform.position - Camera.main.transform.position).normalized;
            transform.LookAt(transform.position + cameraToPosition);
        }
    }
}