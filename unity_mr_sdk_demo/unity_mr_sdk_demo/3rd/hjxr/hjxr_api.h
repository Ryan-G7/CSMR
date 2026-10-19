#ifndef HJXR_API_H
#define HJXR_API_H
#include "hjxr_version.h"
#include "hjxr_type.h"
#include "hjxr_pfn_defines.h"

#include "windows.h"

typedef struct hjxrEnv_s
{
	hjxrPfnTable_t*		pfnTable;
	void*				hDxInterface;
	HMODULE				hSdkDll;
	hjxrSessionHandle	hSession;
}hjxrEnv_t;

typedef hjxrEnv_t*	hjxrEnvHandle;

//NOTE: internal use, do not use this.
static HJXR_FORCE_INLINE hjxrResult __hjxr_initInterface(hjxrEnvHandle* pEnvHandle) {
	static hjxrEnv_t env{};
	if (env.pfnTable == NULL) {
		HMODULE hDLL = LoadLibraryA(HJXR_SDK_DLL_NAME);
		if (hDLL == NULL) {
			return hjxr_dll_failed;
		}
		PFN_hjxrGetInterface pGetInterface = (PFN_hjxrGetInterface)(void*)GetProcAddress(hDLL, HJXR_GET_INTERFACE_FUNC_NAME);
		if (pGetInterface == NULL) {
			FreeLibrary(hDLL);
			return hjxr_dll_wrong;
		}
		env.pfnTable = pGetInterface(HJXR_VERSION_MAJOR, HJXR_VERSION_MINOR);
		if (NULL == env.pfnTable) {
			FreeLibrary(hDLL);
			return hjxr_interface_not_found;
		}
		env.hSdkDll = hDLL;
		env.hSession = env.pfnTable->initSession();
		*pEnvHandle = &env;
	}
	else {
		*pEnvHandle = &env;
	}
	return hjxr_success;
}

//init hjxr Environment.
static HJXR_FORCE_INLINE hjxrResult hjxr_init(hjxrEnvHandle* pEnvHandle) {
	hjxrResult ret = __hjxr_initInterface(pEnvHandle);
	if (ret != hjxr_success) {
		return ret;
	}
	return hjxr_success;
}

//shutdown hjxr Environment.
static HJXR_FORCE_INLINE void hjxr_deinit(hjxrEnvHandle envHandle) {
	if (!envHandle) {
		return;
	}
	if (envHandle->pfnTable && envHandle->hSession){
		envHandle->pfnTable->deinitSession(envHandle->hSession);
	}
	if (envHandle->hSdkDll) {
		FreeLibrary(envHandle->hSdkDll);
		envHandle->hSdkDll = NULL;
	}
	envHandle->pfnTable = NULL;
}

//cmd refer E_HJXR_CMD_type in hjxr_type.h
static HJXR_FORCE_INLINE hjxrResult hjxr_ioctl(hjxrEnvHandle envHandle, int cmd, void* pParam1 = NULL, void* pParam2 = NULL) {
	if (!envHandle || !envHandle->hSession) {
		return hjxr_invalid_param;
	}
	return (hjxrResult)envHandle->pfnTable->ioctl(envHandle->hSession, cmd, pParam1,pParam2);
}

static HJXR_FORCE_INLINE void hjxr_logSetDbgLevel(hjxrEnvHandle envHandle, int level) {
	if (!envHandle) {
		return;
	}
	envHandle->pfnTable->logSetDbgLevel(level);
}

static HJXR_FORCE_INLINE void hjxr_logWrite(hjxrEnvHandle envHandle,int dbgLevel,const char * _Format,...){
	if (!envHandle ) {
		return;
	}
	va_list args;
    va_start(args, _Format);
    envHandle->pfnTable->logWrite(dbgLevel,_Format, args);
    va_end(args);
}


#endif

