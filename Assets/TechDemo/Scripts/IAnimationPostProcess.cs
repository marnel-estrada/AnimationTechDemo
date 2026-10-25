using UnityEngine;
using UnityEngine.Playables;

namespace TechDemo {
    public interface IAnimationPostProcess {
        Playable CreatePlayable(PlayableGraph graph, Animator animator, Playable input);
    }
}