/* UIManager.cs */

using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;
    public Button mainButton;
    public GameObject settingsPage;

    // FPS
    public Text fpsShower;

    // Light Color
    public Slider lightColorR;
    public Slider lightColorG;
    public Slider lightColorB;
    public Text lightColorRT;
    public Text lightColorGT;
    public Text lightColorBT;

    // Water Color
    public Slider waterColorR;
    public Slider waterColorG;
    public Slider waterColorB;
    public Text waterColorRT;
    public Text waterColorGT;
    public Text waterColorBT;

    // Foam
    public Slider upSpeed;
    public Slider downSpeed;
    public Slider foamSize;
    public Text upSpeedT;
    public Text downSpeedT;
    public Text foamSizeT;

    // Light Factors
    public Slider specularity;
    public Slider fresnelPower;
    public Text specularityT;
    public Text fresnelPowerT;

    // Wave
    public Slider windSpeed;
    public Slider waveSpeed;
    public Text windSpeedT;
    public Text waveSpeedT;

    // Debug
    public Toggle debugH;
    public Toggle debugN;
    public Toggle debugRefl;
    public Toggle debugRefr;
    public Toggle debugDx;
    public Toggle debugDy;
    public Toggle debugFoam;

    private void Awake()
    {
        instance = this;
    }

    public void Init()
    {
        mainButton.onClick.RemoveAllListeners();
        mainButton.onClick.AddListener(() => { settingsPage.SetActive(!settingsPage.activeSelf); });

        debugH.onValueChanged.RemoveAllListeners();
        debugH.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugH = debugH.isOn; });

        debugN.onValueChanged.RemoveAllListeners();
        debugN.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugN = debugN.isOn; });

        debugRefl.onValueChanged.RemoveAllListeners();
        debugRefl.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugRefl = debugRefl.isOn; });

        debugRefr.onValueChanged.RemoveAllListeners();
        debugRefr.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugRefr = debugRefr.isOn; });

        debugDx.onValueChanged.RemoveAllListeners();
        debugDx.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugDx = debugDx.isOn; });

        debugDy.onValueChanged.RemoveAllListeners();
        debugDy.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugDy = debugDy.isOn; });

        debugFoam.onValueChanged.RemoveAllListeners();
        debugFoam.onValueChanged.AddListener(isOn => { MeshGPU.instance.debugFoam = debugFoam.isOn; });

        windSpeed.minValue = 0.0f;
        windSpeed.maxValue = 32.0f;
        windSpeed.value = MeshGPU.instance.windSpeed;
        windSpeed.onValueChanged.RemoveAllListeners();
        windSpeed.onValueChanged.AddListener(value => { MeshGPU.instance.windSpeed = value; MeshGPU.instance.reinit = true; });

        waveSpeed.minValue = 0.5f;
        waveSpeed.maxValue = 2.0f;
        waveSpeed.value = MeshGPU.instance.speed;
        waveSpeed.onValueChanged.RemoveAllListeners();
        waveSpeed.onValueChanged.AddListener(value => { MeshGPU.instance.speed = value; });

        lightColorR.minValue = 0.0f;
        lightColorR.maxValue = 1.0f;
        lightColorR.value = MeshGPU.instance.lightColor.r;
        lightColorR.onValueChanged.RemoveAllListeners();
        lightColorR.onValueChanged.AddListener(value => { MeshGPU.instance.lightColor.r = value; });

        lightColorG.minValue = 0.0f;
        lightColorG.maxValue = 1.0f;
        lightColorG.value = MeshGPU.instance.lightColor.g;
        lightColorG.onValueChanged.RemoveAllListeners();
        lightColorG.onValueChanged.AddListener(value => { MeshGPU.instance.lightColor.g = value; });

        lightColorB.minValue = 0.0f;
        lightColorB.maxValue = 1.0f;
        lightColorB.value = MeshGPU.instance.lightColor.b;
        lightColorB.onValueChanged.RemoveAllListeners();
        lightColorB.onValueChanged.AddListener(value => { MeshGPU.instance.lightColor.b = value; });

        waterColorR.minValue = 0.0f;
        waterColorR.maxValue = 1.0f;
        waterColorR.value = MeshGPU.instance.waterColor.r;
        waterColorR.onValueChanged.RemoveAllListeners();
        waterColorR.onValueChanged.AddListener(value => { MeshGPU.instance.waterColor.r = value; });

        waterColorG.minValue = 0.0f;
        waterColorG.maxValue = 1.0f;
        waterColorG.value = MeshGPU.instance.waterColor.g;
        waterColorG.onValueChanged.RemoveAllListeners();
        waterColorG.onValueChanged.AddListener(value => { MeshGPU.instance.waterColor.g = value; });

        waterColorB.minValue = 0.0f;
        waterColorB.maxValue = 1.0f;
        waterColorB.value = MeshGPU.instance.waterColor.b;
        waterColorB.onValueChanged.RemoveAllListeners();
        waterColorB.onValueChanged.AddListener(value => { MeshGPU.instance.waterColor.b = value; });

        upSpeed.minValue = 0.0f;
        upSpeed.maxValue = 10.0f;
        upSpeed.value = MeshGPU.instance.upSpeed;
        upSpeed.onValueChanged.RemoveAllListeners();
        upSpeed.onValueChanged.AddListener(value => { MeshGPU.instance.upSpeed = value; });

        downSpeed.minValue = 0.0f;
        downSpeed.maxValue = 10.0f;
        downSpeed.value = MeshGPU.instance.downSpeed;
        downSpeed.onValueChanged.RemoveAllListeners();
        downSpeed.onValueChanged.AddListener(value => { MeshGPU.instance.downSpeed = value; });

        specularity.minValue = 8.0f;
        specularity.maxValue = 512.0f;
        specularity.value = MeshGPU.instance.specularity;
        specularity.onValueChanged.RemoveAllListeners();
        specularity.onValueChanged.AddListener(value => { MeshGPU.instance.specularity = value; });

        foamSize.minValue = 0.1f;
        foamSize.maxValue = 10.0f;
        foamSize.value = MeshGPU.instance.foamSize;
        foamSize.onValueChanged.RemoveAllListeners();
        foamSize.onValueChanged.AddListener(value => { MeshGPU.instance.foamSize = value; });

        fresnelPower.minValue = 1.0f;
        fresnelPower.maxValue = 100.0f;
        fresnelPower.value = MeshGPU.instance.fresnelPower;
        fresnelPower.onValueChanged.RemoveAllListeners();
        fresnelPower.onValueChanged.AddListener(value => { MeshGPU.instance.fresnelPower = value; });
    }

    private void Update()
    {
        fpsShower.text = ((int)(1.0f / Time.deltaTime)).ToString();

        windSpeedT.text = windSpeed.value.ToString();
        waveSpeedT.text = waveSpeed.value.ToString();
        lightColorRT.text = lightColorR.value.ToString();
        lightColorGT.text = lightColorG.value.ToString();
        lightColorBT.text = lightColorB.value.ToString();
        waterColorRT.text = waterColorR.value.ToString();
        waterColorGT.text = waterColorG.value.ToString();
        waterColorBT.text = waterColorB.value.ToString();
        upSpeedT.text = upSpeed.value.ToString();
        downSpeedT.text = downSpeed.value.ToString();
        specularityT.text = specularity.value.ToString();
        foamSizeT.text = foamSize.value.ToString();
        fresnelPowerT.text = fresnelPower.value.ToString();
    }
}