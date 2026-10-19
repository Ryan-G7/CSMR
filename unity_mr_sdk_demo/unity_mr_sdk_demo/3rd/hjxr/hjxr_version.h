#ifndef HJXR_VERSION_H
#define HJXR_VERSION_H

#define HJXR_MAKE_VERSION(major, minor, patch, build) \
    (((uint64_t)major << 48) | ((uint64_t)minor << 32) | ((uint64_t)patch << 16) | (uint64_t)build)

#define HJXR_VERSION_GET_MAJOR(version) ((version >> 48) & 0xffff)
#define HJXR_VERSION_GET_MINOR(version) ((version >> 32) & 0xffff)
#define HJXR_VERSION_GET_PATCH(version) ((version >> 16) & 0xffff)
#define HJXR_VERSION_GET_BUILD(version) (version & 0xffff)

#define HJXR_VERSION_MAJOR 5
#define HJXR_VERSION_MINOR 0
#define HJXR_VERSION_PATCH 0
#define HJXR_VERSION_BUILD 0

#define HJXR_VERSION HJXR_MAKE_VERSION(HJXR_VERSION_MAJOR, HJXR_VERSION_MINOR, HJXR_VERSION_PATCH, HJXR_VERSION_BUILD)
#endif
