using UnityEngine;

public class ThrusterFXController : MonoBehaviour
{
    [Header("References")]
    public KeyboardRigMover rigMover;
    public ParticleSystem[] thrusterJets;
    public Light[] thrusterLights;

    [Header("Particle Settings")]
    public float minEmissionRate = 5f;
    public float maxEmissionRate = 35f;

    [Header("Light Settings")]
    public float maxLightIntensity = 1.5f;

    [Header("Response")]
    public float smoothing = 8f;

    private float _smoothedIntensity = 0f;

    private void Start()
    {
        if (rigMover == null)
        {
            rigMover = GetComponentInParent<KeyboardRigMover>();
        }
    }

    private void Update()
    {
        if (rigMover == null)
            return;

        float target = Mathf.Clamp01(rigMover.CurrentThrustIntensity);
        float lerp = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        _smoothedIntensity = Mathf.Lerp(_smoothedIntensity, target, lerp);

        UpdateParticles();
        UpdateLights();
    }

    private void UpdateParticles()
    {
        if (thrusterJets == null)
            return;

        float emissionValue = Mathf.Lerp(minEmissionRate, maxEmissionRate, _smoothedIntensity);

        for (int i = 0; i < thrusterJets.Length; i++)
        {
            ParticleSystem ps = thrusterJets[i];
            if (ps == null) continue;

            var emission = ps.emission;
            emission.rateOverTime = emissionValue;

            var main = ps.main;
            main.startSpeed = Mathf.Lerp(0.1f, 1.5f, _smoothedIntensity);

            if (!ps.isPlaying && _smoothedIntensity > 0.05f)
            {
                ps.Play();
            }
            else if (ps.isPlaying && _smoothedIntensity <= 0.01f)
            {
                ps.Stop();
            }
        }
    }

    private void UpdateLights()
    {
        if (thrusterLights == null)
            return;

        float intensity = maxLightIntensity * _smoothedIntensity;
        for (int i = 0; i < thrusterLights.Length; i++)
        {
            Light light = thrusterLights[i];
            if (light == null) continue;

            light.intensity = intensity;
            light.enabled = intensity > 0.01f;
        }
    }
}
