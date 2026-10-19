#ifndef HJXR_TYPE_H
#define HJXR_TYPE_H
#include <cstdint>
#include "hjxr_version.h"

#define HJXR_FORCE_INLINE __inline

#if !defined(HJXR_PTR_SIZE)
#if defined(__WORDSIZE)
#define HJXR_PTR_SIZE ((__WORDSIZE) / 8)
#elif defined(_WIN64) || defined(__LP64__) || defined(_LP64) || defined(_M_IA64) || defined(__ia64__) || defined(__arch64__) || defined(__64BIT__) || defined(__Ptr_Is_64)
#define HJXR_PTR_SIZE 8
#elif defined(__CC_ARM) && (__sizeof_ptr == 8)
#define HJXR_PTR_SIZE 8
#else
#define HJXR_PTR_SIZE 4
#endif
#endif

//-----------------------------------------------------------------------------------
// ***** HJXR_ALIGNAS
//
#if !defined(HJXR_ALIGNAS)
#if defined(__GNUC__) || defined(__clang__)
#define HJXR_ALIGNAS(n) __attribute__((aligned(n)))
#elif defined(_MSC_VER) || defined(__INTEL_COMPILER)
#define HJXR_ALIGNAS(n) __declspec(align(n))
#elif defined(__CC_ARM)
#define HJXR_ALIGNAS(n) __align(n)
#else
#error Need to define HJXR_ALIGNAS
#endif
#endif

// result code.
typedef enum 
{
	hjxr_success = 0,
	hjxr_failed = -1,
	hjxr_dll_failed = -2,
	hjxr_dll_wrong = -3,
	hjxr_interface_not_found = -4,
	hjxr_invalid_param = -5,
	hjxr_rpc_failed = -6,
	hjxr_share_mem_failed = -7,
	hjxr_unsupport_render_name = -8,
	hjxr_no_display = -9,
	hjxr_no_render_device = -10,
	hjxr_app_not_visible = -11,
	hjxr_srv_not_ready = -12,
	hjxr_dll_srv_mismatch = -13,
	hjxr_app_adapter_mismatch = -14,
	hjxr_not_support = -15,
	
	// Pipe error codes
	hjxr_pipe_invalid_handle = -100,
	hjxr_pipe_broken = -101,
	hjxr_pipe_read_failed = -102,
	hjxr_pipe_overlapped_failed = -103,
	hjxr_pipe_timeout = -104,
	hjxr_pipe_memory = -105,
	hjxr_pipe_mutex = -106,
	hjxr_pipe_unknown = -107,
}hjxrResult;

enum HJXR_DBG_LEVEL_e {
	HJXR_DBG_LEVEL_FATAL	=	0,
	HJXR_DBG_LEVEL_ERR		=	1,
	HJXR_DBG_LEVEL_WARN 	=	2,
	HJXR_DBG_LEVEL_DBG		=	3,
	HJXR_DBG_LEVEL_INFO 	=	4,
	HJXR_DBG_LEVEL_VERBOSE	=	5,
};

//eye type
typedef enum
{
	hjxrEye_Left = 0,
	hjxrEye_Right = 1,
	hjxrEye_Count = 2
} hjxrEyeType_e;

typedef void* hjxrSessionHandle;


typedef int32_t hjxr_Bool;
static const hjxr_Bool hjxr_False = 0;
static const hjxr_Bool hjxr_True = 1;

/**
 * Camera property data types.
 */
typedef int64_t hjxr_CameraPropertyDataType;
static const hjxr_CameraPropertyDataType hjxr_CameraPropertyDataType_Int = 1;     //!< Integer.
static const hjxr_CameraPropertyDataType hjxr_CameraPropertyDataType_Double = 2;  //!< Floating point.
static const hjxr_CameraPropertyDataType hjxr_CameraPropertyDataType_Bool = 3;    //!< Boolean.

/**
 * Camera property types that can be accessed to control video pass through cameras.
 *
 * See #hjxr_MRGetCameraPropertyModes and #hjxr_MRGetCameraPropertyValues documentation for details.
 */
typedef int64_t hjxr_CameraPropertyType;
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_ExposureTime = 1;         //!< Exposure time.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_ISOValue = 2;             //!< ISO value.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_WhiteBalance = 3;         //!< White balance.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_FlickerCompensation = 4;  //!< Flicker compensation (50Hz and 60Hz).
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_Sharpness = 5;            //!< Sharpness filter mode.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_EyeReprojection = 6;      //!< Reproject the image to correspond the physical position of user's eyes.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_IspExposureTime = 7;      //!< isp Exposure time.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_IspISOValue = 8;          //!< isp ISO value.
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_Focus_Left = 9;           //!< Focus Left
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_Focus_Right = 10;         //!< Focus Right
static const hjxr_CameraPropertyType hjxr_CameraPropertyType_ExposureTimeAutoAE = 11;         //!< AE
/**
 * Camera property modes that can be set to control video pass through cameras.
 *
 * See #hjxr_MRGetCameraPropertyModes documentation for details.
 */
typedef int64_t hjxr_CameraPropertyMode;
static const hjxr_CameraPropertyMode hjxr_CameraPropertyMode_Off = 0;     //!< Off mode.
static const hjxr_CameraPropertyMode hjxr_CameraPropertyMode_Auto = 1;    //!< Automatic mode.
static const hjxr_CameraPropertyMode hjxr_CameraPropertyMode_Manual = 2;  //!< Manual value mode.


/**
 * Wrapper for different camera property values.
 */
union hjxr_CameraValue {
    double doubleValue;    //!< Floating point value.
    int64_t intValue;      //!< Integer value.
    hjxr_Bool boolValue;  //!< Boolean value.
};

/**
 * Camera property value structure including type info and actual value.
 */
struct hjxr_CameraPropertyValue {
    hjxr_CameraPropertyDataType type;  //!< Data type for this value.
    union hjxr_CameraValue value;      //!< Value union. Determine from 'type' which element to access.
};

struct hjxr_CameraPorpInfo{
	hjxr_CameraPropertyType type;
	hjxr_CameraPropertyValue	value;
};

enum E_HJxr_Layer_type{
	E_HJXR_LAYER_VR		=	0,
	E_HJXR_LAYER_VST		=	1,
	E_HJXR_LAYER_MAX
};
struct hjxr_LayerOrderInfo{
	E_HJxr_Layer_type 	layerType;
	int 				zorder;
};
struct hjxr_LayerEnableInfo{
	E_HJxr_Layer_type 	layerType;
	int 				enable;
};
struct hjxr_LayerPoseTimeOffsetInfo{
	E_HJxr_Layer_type 	layerType;
	int64_t 			timeOffsetNs;
};

/*
 * 32bit floating point 2D vector.
 */
#pragma pack(push, 1)
struct hjxr_Vector2f {
	float x;  //!< X coordinate.
	float y;  //!< Y coordinate.
};
#pragma pack(pop)

/*
 * 32bit floating point 3D vector.
 */
#pragma pack(push, 1)
struct hjxr_Vector3 {
    float x;  //!< X coordinate.
    float y;  //!< Y coordinate.
    float z;  //!< Z coordinate.
};
#pragma pack(pop)

typedef hjxr_Vector3 hjxr_Position;

//! \brief Simple structure to hold quaternion data
#pragma pack(push, 1)
struct hjxr_Quaternion
{
    float x, y, z, w;
};
#pragma pack(pop)

#pragma pack(push, 1)
struct hjxr_PoseInfo
{
    hjxr_Vector3      	position;
    hjxr_Quaternion   	rotation;
};
#pragma pack(pop)

#pragma pack(push, 1)
struct hjxr_PoseInfoEuler
{
    hjxr_Vector3 rotation; // angle degree
    hjxr_Vector3 position;
};
#pragma pack(pop)

#define HJXR_SZ_VST_NIGHT_MODE_INFO		sizeof(hjxr_VstNightModeInfo)
#pragma pack(push, 1)
struct hjxr_VstNightModeInfo{
	bool 	enable;
	float	brightness;
	float	contrast;
	bool	greenFilter;
};
#pragma pack(pop)

struct hjxr_Matrix {
    double value[16];
};
struct hjxr_Matrixf {
    float value[16];
};

typedef struct HJXR_ALIGNAS(8) hjxr_EyeTrackingData_s {
	hjxr_Vector2f  GazeTan[hjxrEye_Count]; //tan of eye gaze.
	double TimeInSeconds;
} hjxr_EyeTrackingData_t;

enum E_HJXR_CMD_type{
	E_HJXR_CMD_SET_LAYER_ZORDER						= 0,//set layer zoder,param1 is hjxr_LayerOrderInfo*
	E_HJXR_CMD_GET_LAYER_ZORDER						= 1,//get layer zorder

	E_HJXR_CMD_SET_LAYER_ENABLE						= 2,//set layer enable/disable ,param1 is hjxr_LayerEnableInfo*
	E_HJXR_CMD_GET_LAYER_ENABLE_STATE				= 3,//get layer enable state 

	E_HJXR_CMD_SET_VST_MESH_WITH_DEPTH_ENABLE 		= 4,//set whether vst layer's mesh with depth,param1 is int*(0 or 1)
	E_HJXR_CMD_GET_VST_MESH_WITH_DEPTH_ENABLE_STATE	= 5,//get the state whether vst layer's mesh with depth

	E_HJXR_CMD_SET_CHROMA_KEY_ENABLE					= 6,//set chroma key enable/disable ,param1 int*(0 or 1)
	E_HJXR_CMD_GET_CHROMA_KEY_ENABLE_STATE			= 7,//get chroma key enable state

	E_HJXR_CMD_SET_CHROMA_KEY_COLOR 					= 8,//set chroma color for vr layer,param1 is rgba8888 uint32_t
	E_HJXR_CMD_GET_CHROMA_KEY_COLOR 					= 9,//get chroma color for vr layer

	E_HJXR_CMD_SET_VST_ZONE_BY_3DOBJ_ENABLE			= 10,//set transparent zone by 3d object enable/disable ,param1 int*(0 or 1)
	E_HJXR_CMD_GET_VST_ZONE_BY_3DOBJ_ENABLE_STATE 	= 11,//get transparent zone by 3d object enable state

	E_HJXR_CMD_SET_VST_ZONE_3DOB_FILE_PATH 			= 12,//set the 3D object file path for transparent zone,param1 is char*,max size is 256 bytes
	E_HJXR_CMD_GET_VST_ZONE_3DOB_FILE_PATH 			= 13,//get the 3D object file path for transparent zone

	E_HJXR_CMD_SET_CAMERA_PROP						= 14,//set camera prop, param1 is hjxr_CameraPorpInfo*
	E_HJXR_CMD_GET_CAMERA_PROP						= 15,//get camera prop

	E_HJXR_CMD_WORLD_SYNC							= 16,//start marker sync

	E_HJXR_CMD_WORLD_SET_MARKER_POSE_OFFSET			= 17,//set marker pose offset, param1 is hjxr_PoseInfo* 
	E_HJXR_CMD_WORLD_GET_MARKER_POSE_OFFSET			= 18,//get marker pose offset

	E_HJXR_CMD_SET_VST_MODEL_POSITION_OFFSET			= 19,//set vst model position offset,param1 is hjxr_Position*  // no use, but do not delete
	E_HJXR_CMD_GET_VST_MODEL_POSITION_OFFSET			= 20,//get vst model position offset  // no use, but do not delete

	E_HJXR_CMD_SET_LAYER_POSE_TIME_OFFSET			= 21,//set layer pose time offset, param1 is hjxr_LayerPoseTimeOffsetInfo*
	E_HJXR_CMD_GET_LAYER_POSE_TIME_OFFSET			= 22,//get layer pose time offset

	E_HJXR_CMD_SAVE_CONFIG_TO_FILE					= 23,//save configs to file

	E_HJXR_CMD_GET_MARKER_ADJ_MAT					= 24,//get marker adjust matrix,param1 is hjxr_Matrix*
	E_HJXR_CMD_GET_MARKER_ADJ_MAT_ADDR				= 25,//get marker adjust matrix addr,param1 is hjxr_Matrix**

	E_HJXR_CMD_SET_VST_VSYNC_OFFSET_NS				= 26,//set vst vsync offset ns time,param1 is uint64_t*
	E_HJXR_CMD_GET_VST_VSYNC_OFFSET_NS				= 27,//get vst vsync offset ns time

	E_HJXR_CMD_SET_VST_NIGHT_MODE					= 28,//set vst night mode,param1 is hjxr_VstNightModeInfo*
	E_HJXR_CMD_GET_VST_NIGHT_MODE					= 29,//get vst night mode,param1 is hjxr_VstNightModeInfo*

	E_HJXR_CMD_SET_MASK_MODEL_POSE_OFFSET			= 30,//set mask model's pose offset, param1 is hjxr_PoseInfo* 
	E_HJXR_CMD_GET_MASK_MODEL_POSE_OFFSET			= 31,//get mask model's pose offset

	E_HJXR_CMD_SET_CHROMA_KEY_THRESHOLD				= 32,//set chroma color threshold,param1 is rgba888 uint32_t, with E_HJXR_CMD_SET_CHROMA_KEY_COLOR
	E_HJXR_CMD_GET_CHROMA_KEY_THRESHOLD				= 33,//get chroma color threshold

	E_HJXR_CMD_SET_VIDEO_MODEL_POSE_OFFSET			= 34,//set video model's pose offset, param1 is hjxr_PoseInfo* , used for test adjustment, not system init
	E_HJXR_CMD_GET_VIDEO_MODEL_POSE_OFFSET			= 35,//no use

	E_HJXR_CMD_SET_MIXREALITY_TRANS					= 36,//set mixreality translationoffset, param1 is jxr_Position* , used for test adjustment, not system init
	E_HJXR_CMD_GET_MIXREALITY_TRANS					= 37,//no use

	E_HJXR_CMD_SET_DFR_CROP_TANXY					= 38, // transfer eye tracking tanxy to hjserver

	E_HJXR_CMD_SET_CAMERA_FRAME_BUFFER_ENABLE		= 39, // enable camera frame buffer, chose to use last frame or current frame;
	E_HJXR_CMD_GET_CAMERA_FRAME_BUFFER_ENABLE		= 40,
	
	E_HJXR_CMD_SET_TRACK_LOCATE_SPACE_ENABLE		= 41, // enable trackLocateSpace;
	E_HJXR_CMD_GET_TRACK_LOCATE_SPACE_ENABLE		= 42,
	
	E_HJXR_CMD_SET_LAYER_POSE_DEFER					= 43, // layerPoseDefer;
	E_HJXR_CMD_GET_LAYER_POSE_DEFER					= 44,

	E_HJXR_CMD_SET_RS_CAMERA_CORRECTION_ENABLE		= 45, // enable rs camera correction;
	E_HJXR_CMD_GET_RS_CAMERA_CORRECTION_ENABLE		= 46,

	E_HJXR_CMD_GET_EYE_TRACKING_DATA				= 47,// get eye tracking data, param1 is hjxr_EyeTrackingData_t*

	E_HJXR_CMD_SET_ALIGN_METHOD						= 48, // set mConfigCoreMarker.alignMethod 0 1 2  3 ["QR code", "map", "controller", "tracker"]

	E_HJXR_CMD_SET_VST_DFR_ENABLE					= 49, // set vst dfr enable/disable ,param1 is int*(0 or 1)
	E_HJXR_CMD_SET_VR_WIDTHHEIGHT_PECT				= 50, // set vr layer width height percent , param1 is int*(10-100)

	E_HJXR_CMD_SET_CLOSE_SERVER						= 51, // close hjxr server
	E_HJXR_CMD_GET_SVR_GLOBAL_INI_INFO				= 52, // get hjxr server global ini info, param1 is jxr_svr_global_ini_info_t*

	E_HJXR_CMD_SET_CAMERA_BicubicEnable				= 53, // set camera bicubic enable/disable ,param1 is int*(0 or 1)
	E_HJXR_CMD_SET_CAMERA_BrightFactor				= 54, // set camera bright factor , param1 is float*
	E_HJXR_CMD_SET_CAMERA_GammaEnable				= 55, // set camera gamma enable/disable ,param1 is int*(0 or 1)	
	E_HJXR_CMD_SET_CAMERA_GammaFactor				= 56, // set camera gamma factor , param1 is float*

	E_HJXR_CMD_TRACKER_DETECT_MARKER				= 57, // use tracker align, detect marker firstly, no data
	E_HJXR_CMD_TRACKER_POSE_RECORD					= 58, // trakcer pose record, param1 is int (1,2)

	E_HJXR_CMD_SET_MARKER_VIVE_OFFSET				= 59, // marker to vive offset on frock

	E_HJXR_CMD_SET_HEAD_LOCK						= 60, // vr head lock or unlock
	E_HJXR_CMD_SET_USE_CUBE_MARKER   				= 61, // bool use cobe marker

	E_HJXR_CMD_GET_SVR_CAMERA_CALIBRATION_INFO		= 62, // get hjxr server camera calibration info, param1 is hjsvr_cameras_calibration_data_t*

	E_HJXR_CMD_WORLD_SET_MARKER_POSE				= 63,//set marker pose, param1 is hjxr_PoseInfo* 
	E_HJXR_CMD_WORLD_GET_MARKER_POSE				= 64,//get marker pose

	E_HJXR_CMD_Unknown
};


#endif
