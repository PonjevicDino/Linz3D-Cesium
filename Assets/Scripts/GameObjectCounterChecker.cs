using UnityEngine;

public class GameObjectCounterChecker : MonoBehaviour
{
    [Tooltip("The GameObject to disable/enable when the limit is exceeded")]
    public GameObject targetGameObject;

    [Tooltip("Maximum allowed GameObjects in the scene")]
    public int maxObjectsLimit = 1000;
}