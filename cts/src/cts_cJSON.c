#include "cts_cJSON.h"
#include "cts_array.h"

#include <limits.h>
#include <string.h>

static cJSON *serialize_value(const cts_type *type, const void *data)
{
    switch ((cts_value_kind)type->flags) {
    case CTS_VALUE_CHAR: return cJSON_CreateNumber(*(const int8_t *)data);
    case CTS_VALUE_UCHAR: return cJSON_CreateNumber(*(const uint8_t *)data);
    case CTS_VALUE_INT16: return cJSON_CreateNumber(*(const int16_t *)data);
    case CTS_VALUE_UINT16: return cJSON_CreateNumber(*(const uint16_t *)data);
    case CTS_VALUE_INT32: return cJSON_CreateNumber(*(const int32_t *)data);
    case CTS_VALUE_UINT32: return cJSON_CreateNumber(*(const uint32_t *)data);
    case CTS_VALUE_INT64: return cJSON_CreateNumber((double)*(const int64_t *)data);
    case CTS_VALUE_UINT64: return cJSON_CreateNumber((double)*(const uint64_t *)data);
    case CTS_VALUE_FLOAT: return cJSON_CreateNumber(*(const float *)data);
    case CTS_VALUE_DOUBLE: return cJSON_CreateNumber(*(const double *)data);
    case CTS_VALUE_BOOL: return cJSON_CreateBool(*(const bool *)data);
    case CTS_VALUE_CSTR: {
        const char *s = *(char *const *)data;
        return s ? cJSON_CreateString(s) : cJSON_CreateNull();
    }
    default: return NULL;
    }
}

cJSON *cts_cJSON_serialize(const cts_type *type, const void *data)
{
    if (!type || !data) return NULL;
    if (type->kind == CTS_TYPE_VALUE) return serialize_value(type, data);
    if (type->kind == CTS_TYPE_ENUM) {
        const char *name = cts_enum_to_name(type, *(const int *)data);
        return name ? cJSON_CreateString(name) : NULL;
    }
    if (type->kind == CTS_TYPE_STRUCT) {
        cJSON *object = cJSON_CreateObject();
        const cts_field *fields = (const cts_field *)type->fields;
        for (size_t i = 0; object && i < type->field_count; ++i) {
            cJSON *value = cts_cJSON_serialize(fields[i].type,
                                              (const char *)data + fields[i].offset);
            if (!value || !cJSON_AddItemToObject(object, fields[i].name, value)) {
                cJSON_Delete(value);
                cJSON_Delete(object);
                return NULL;
            }
        }
        return object;
    }
    if (type->kind == CTS_TYPE_COLLECTION) {
        cJSON *array = cJSON_CreateArray();
        cts_iterator *it = type->get_iterator(type, (void *)data);
        if (!array || !it) { cJSON_Delete(array); return NULL; }
        while (it->has_next(it)) {
            cJSON *value = cts_cJSON_serialize(type->item_type, it->get_value(it));
            if (!value || !cJSON_AddItemToArray(array, value)) {
                cJSON_Delete(value); cJSON_Delete(array); it->free(it); return NULL;
            }
            it->move_next(it);
        }
        it->free(it);
        return array;
    }
    return NULL;
}

static bool deserialize_into(const cts_type *type, const cJSON *json, void *out)
{
    if (type->kind == CTS_TYPE_VALUE) {
        if ((cts_value_kind)type->flags == CTS_VALUE_CSTR) {
            if (cJSON_IsNull(json)) { *(char **)out = NULL; return true; }
            if (!cJSON_IsString(json)) return false;
            size_t n = strlen(json->valuestring) + 1;
            char *s = (char *)cts_malloc(n);
            if (!s) return false;
            memcpy(s, json->valuestring, n);
            *(char **)out = s;
            return true;
        }
        if ((cts_value_kind)type->flags == CTS_VALUE_BOOL) {
            if (!cJSON_IsBool(json)) return false;
            *(bool *)out = cJSON_IsTrue(json); return true;
        }
        if (!cJSON_IsNumber(json)) return false;
        switch ((cts_value_kind)type->flags) {
        case CTS_VALUE_CHAR: *(int8_t *)out = (int8_t)json->valuedouble; break;
        case CTS_VALUE_UCHAR: *(uint8_t *)out = (uint8_t)json->valuedouble; break;
        case CTS_VALUE_INT16: *(int16_t *)out = (int16_t)json->valuedouble; break;
        case CTS_VALUE_UINT16: *(uint16_t *)out = (uint16_t)json->valuedouble; break;
        case CTS_VALUE_INT32: *(int32_t *)out = (int32_t)json->valuedouble; break;
        case CTS_VALUE_UINT32: *(uint32_t *)out = (uint32_t)json->valuedouble; break;
        case CTS_VALUE_INT64: *(int64_t *)out = (int64_t)json->valuedouble; break;
        case CTS_VALUE_UINT64: *(uint64_t *)out = (uint64_t)json->valuedouble; break;
        case CTS_VALUE_FLOAT: *(float *)out = (float)json->valuedouble; break;
        case CTS_VALUE_DOUBLE: *(double *)out = json->valuedouble; break;
        default: return false;
        }
        return true;
    }
    if (type->kind == CTS_TYPE_ENUM) {
        if (cJSON_IsString(json)) { *(int *)out = cts_enum_to_value(type, json->valuestring); return true; }
        if (cJSON_IsNumber(json)) { *(int *)out = json->valueint; return true; }
        return false;
    }
    if (type->kind == CTS_TYPE_STRUCT && cJSON_IsObject(json)) {
        const cts_field *fields = (const cts_field *)type->fields;
        for (size_t i = 0; i < type->field_count; ++i) {
            const cJSON *item = cJSON_GetObjectItemCaseSensitive(json, fields[i].name);
            if (item && !deserialize_into(fields[i].type, item,
                                          (char *)out + fields[i].offset)) return false;
        }
        return true;
    }
    if (type->kind == CTS_TYPE_COLLECTION && cJSON_IsArray(json)) {
        cts_array *array = (cts_array *)out;
        cts_array_init(array, type->item_type);
        int count = cJSON_GetArraySize(json);
        for (int i = 0; i < count; ++i) {
            void *item = cts_new_instance(type->item_type);
            if (!item || !deserialize_into(type->item_type, cJSON_GetArrayItem(json, i), item) ||
                !cts_array_push(array, item)) { cts_free(item); cts_array_dispose(array); return false; }
            cts_free(item);
        }
        return true;
    }
    return false;
}

void *cts_cJSON_deserialize(const cts_type *type, const cJSON *json)
{
    void *result = cts_new_instance(type);
    if (!result) return NULL;
    if (!deserialize_into(type, json, result)) {
        cts_free_instance(type, result);
        return NULL;
    }
    return result;
}
