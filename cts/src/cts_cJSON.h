#ifndef CTS_CJSON_H
#define CTS_CJSON_H

#include "cJSON.h"
#include "cts_core.h"

CTS_EXTERN cJSON *cts_cJSON_serialize(const cts_type *type, const void *data);
CTS_EXTERN void *cts_cJSON_deserialize(const cts_type *type, const cJSON *json);

#endif
