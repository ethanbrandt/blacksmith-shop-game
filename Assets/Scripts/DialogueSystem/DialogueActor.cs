using UnityEngine;

namespace DialogueSystem
{
    public abstract class DialogueActor : MonoBehaviour
    {
        public abstract void SetPose(string _poseName);
    }
}