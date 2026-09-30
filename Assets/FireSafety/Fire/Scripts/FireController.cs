
using UnityEngine;

public class FireController : MonoBehaviour
{
    [Header("Fire Particle Systems")]
    [SerializeField] private ParticleSystem fire1;
    [SerializeField] private ParticleSystem fire2;
    [SerializeField] private ParticleSystem fire3;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private ParticleSystem smoke;

    [Header("Extinguishing")]
    [SerializeField] private float extinguishDuration = 5f;

    private ParticleSystem[] systems;
    private float[] initialRates;
    private float extinguishProgress;
    private bool isExtinguished;

    private void Awake()
    {
        systems = new ParticleSystem[]
        {
            fire1, fire2, fire3, sparks, smoke
        };

        initialRates = new float[systems.Length];

        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;

            var emission = systems[i].emission;
            initialRates[i] = emission.rateOverTime.constant;
        }
    }

    public void ApplyExtinguishing(float amount)
    {
        if (isExtinguished || amount <= 0f)
            return;

        extinguishProgress += amount;

        float progress = Mathf.Clamp01(
            extinguishProgress / extinguishDuration
        );

        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;

            var emission = systems[i].emission;
            emission.rateOverTime = Mathf.Lerp(
                initialRates[i], 0f, progress
            );
        }

        if (progress >= 1f)
        {
            isExtinguished = true;

            foreach (var system in systems)
            {
                if (system != null)
                    system.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmitting
                    );
            }
        }
    }
}