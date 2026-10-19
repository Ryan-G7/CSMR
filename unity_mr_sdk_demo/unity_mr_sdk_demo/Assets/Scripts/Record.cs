using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Windows.Speech;

//坐标系同步Demo，运行后按K键或者说“对齐”语音命令,即可将VR世界的坐标系和真实世界的坐标系完全同步

//1.VR坐标系中指定一个物体mVirtualMarkerObject(虚拟marker)
//2.将其位姿值transform通过Ioctl(WorldSetMarkerPose,...)接口设置给hjxr_sdk,
//3.调用Ioctl(WorldSync)接口，hjxr_sdk会将VR世界的坐标系和真实世界的坐标系完全同步 
public class Record : MonoBehaviour
{
    [SerializeField]
    public GameObject mGameObject;

    [SerializeField]
    public Renderer mObjectRenderer; // 用于获取对象的渲染器

    [SerializeField]
    public GameObject mVirtualMarkerObject;


    private PhraseRecognizer m_PhraseRecognizer;
    public string[] keywords = { "旋转", "缩放","变色","对齐" };
    public ConfidenceLevel m_confidenceLevel = ConfidenceLevel.Medium;

    //private int mFlag = 0;
    private bool mNeedRotate = true;
    private Color32 mColor = Color.white;

    private Color32[] mColorArr = { Color.white, Color.red, Color.yellow, Color.blue, Color.green, Color.cyan };
    private int mColorIdx = 0;

    public float mScaleRatio = 5.0f;
    public bool mIsScaleUp = true;
    public bool mIsNeedScale = false;
    public bool mIsNeedWorldSync = false;

    private HjxrPoseInfo mVirtualMarkerPose;

    private void Awake()
    {
        mVirtualMarkerPose = new HjxrPoseInfo();
        mVirtualMarkerPose.rotation.w = 1.0f;
    }


    void Start()
    {
        Debug.Log("Start i");

        // Hide the virtual marker object
        //mVirtualMarkerObject.GetComponent<MeshRenderer>().enabled = false;

        try
        {
            HjxrResult initResult = HjxrNative.Init();
            Debug.Log("HjxrInit result: " + initResult);
            if (initResult == HjxrResult.Success)
            {
                //获取VR世界中虚拟marker对应的位姿
                //Unity 坐标系转OpenXR坐标系pos.z取负,rot.z和rot.w取负
                mVirtualMarkerPose.position.x = mVirtualMarkerObject.transform.position.x;
                mVirtualMarkerPose.position.y = mVirtualMarkerObject.transform.position.y;
                mVirtualMarkerPose.position.z = -mVirtualMarkerObject.transform.position.z;
                mVirtualMarkerPose.rotation.x = mVirtualMarkerObject.transform.rotation.x;
                mVirtualMarkerPose.rotation.y = mVirtualMarkerObject.transform.rotation.y;
                mVirtualMarkerPose.rotation.z = -mVirtualMarkerObject.transform.rotation.z;
                mVirtualMarkerPose.rotation.w = -mVirtualMarkerObject.transform.rotation.w;

                //设置pose
                HjxrResult poseResult = HjxrNative.Ioctl(HjxrCmdType.WorldSetMarkerPose, ref mVirtualMarkerPose);
                Debug.Log("set marker pose:" + mVirtualMarkerPose.position.x + "," + mVirtualMarkerPose.position.y + "," + mVirtualMarkerPose.position.z + " " +
                    mVirtualMarkerPose.rotation.x + "," + mVirtualMarkerPose.rotation.y + "," + mVirtualMarkerPose.rotation.z + "," + mVirtualMarkerPose.rotation.w +
                    " result: " + poseResult);

            }
        }
        catch (Exception e)
        {
            Debug.LogError("HjxrInit failed: " + e.Message);
        }
        if (m_PhraseRecognizer == null)
        {
            m_PhraseRecognizer = new KeywordRecognizer(keywords, m_confidenceLevel);
            m_PhraseRecognizer.OnPhraseRecognized += M_PhraseRecognizer_OnPhraseRecognized;
            m_PhraseRecognizer.Start();
        }

        Debug.Log("Start o");

    }
    private void OnDestroy()
    {
        Debug.Log("Destroy i");
        HjxrNative.Deinit();
        Debug.Log("HjxrDeinit called");
        Debug.Log("Destroy o");
    }

    private void M_PhraseRecognizer_OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        switch (args.text)
        {
            case "旋转":
                //mFlag = 0;
                mNeedRotate = !mNeedRotate;
                break;
            case "缩放":
                mIsNeedScale = !mIsNeedScale;
                break;
            case "变色":
                //mFlag = 3;
                mColor = mColorArr[mColorIdx++];
                mColorIdx = mColorIdx % mColorArr.Length;
                mObjectRenderer.material.color = mColor;
                break;
            case "对齐":
                mIsNeedWorldSync = true;
                break;
            default:

                break;
        }

        Debug.Log("语音识别:" + args.text);
    }
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Debug.Log("Esc pressed,exit");
            Application.Quit();
        }

        if (Input.GetKeyUp(KeyCode.K))
        {
            mIsNeedWorldSync = true;
            Debug.Log("K pressed");
        }

        if (mNeedRotate) { 
            mGameObject.transform.Rotate(new Vector3(0, -0.2f, 0));   // 绕着自身的坐标轴转
        }
        if (mIsNeedScale) {
            mGameObject.transform.localScale = new Vector3(mScaleRatio, mScaleRatio, mScaleRatio);
            if (mScaleRatio > 10f) {
                mIsScaleUp = false;
            } else if (mScaleRatio < 1.0f) {
                mIsScaleUp = true;
            }

            if (mIsScaleUp)
            {
                mScaleRatio += 0.01f;
            }
            else {
                mScaleRatio -= 0.01f;
            }

        }
        if (mIsNeedWorldSync)
        {
            try {
                //将VR世界的坐标系和真实世界的坐标系完全同步
                HjxrResult result = HjxrNative.Ioctl(HjxrCmdType.WorldSync);
                Debug.Log("WorldSync result: " + result);
                mIsNeedWorldSync = false;
            }
            catch (Exception e)
            {
                Debug.LogError("HjxrInit failed: " + e.Message);
            }
        }
    }
}
