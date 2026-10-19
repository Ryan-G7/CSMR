#ifndef HJXR_PFN_DEFINES_H
#define HJXR_PFN_DEFINES_H
#include "hjxr_version.h"
#include "hjxr_type.h"

#if HJXR_PTR_SIZE == 8
#define HJXR_SDK_DLL_NAME "jxrsdk.dll"
#else
#define HJXR_SDK_DLL_NAME "jxrsdk32.dll"
#endif

#define HJXR_GET_INTERFACE_FUNC_NAME "jxr_hjxrGetInterface"

typedef hjxrSessionHandle (*PFN_hjxrSessionInit)();
typedef void (*PFN_hjxrSessionDeinit)(hjxrSessionHandle hnd);
typedef int (*PFN_hjxrIoctl)(hjxrSessionHandle hnd, int cmd, void* pParam1, void* pParam2);
typedef char* (*PFN_hjxrGetVersionString)();
typedef uint64_t (*PFN_hjxrGetVersion)();
typedef void (*PFN_hjxrLogSetDbgLevel)(int dbgLevel);
typedef void (*PFN_hjxrLogWrite)(int dbgLevel,const char * _Format, ...);

typedef struct hjxrPfnTable_s
{
	PFN_hjxrSessionInit initSession;
	PFN_hjxrSessionDeinit deinitSession;
	PFN_hjxrIoctl ioctl;
	PFN_hjxrGetVersionString getVersionString;
	PFN_hjxrGetVersion getVersion;
	PFN_hjxrLogSetDbgLevel logSetDbgLevel;
	PFN_hjxrLogWrite logWrite;
}hjxrPfnTable_t;
	
typedef hjxrPfnTable_t* (*PFN_hjxrGetInterface)(uint32_t major_ver, uint32_t minor_ver);

#endif

