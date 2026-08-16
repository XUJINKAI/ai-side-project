#include "cts_array.h"
#include "cts_cJSON.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>

typedef enum Gender { GENDER_UNKNOWN, GENDER_MALE, GENDER_FEMALE } Gender;

CTS_ENUM_DECLARE(static, Gender);
CTS_ENUM_IMPLEMENT(static, Gender,
    EXPAND({GENDER_MALE, "Male"}, {GENDER_FEMALE, "Female"}),
    EXPAND({GENDER_UNKNOWN, "Unknown"}), CTS_FLAG_IGNORE_CASE);

typedef struct Student {
    int32_t id;
    char *name;
    Gender gender;
    bool active;
} Student;

static const cts_field student_fields[] = {
    {offsetof(Student, id), "id", &CTS_INNER_TYPE(int32), CTS_FLAG_NONE, NULL},
    {offsetof(Student, name), "name", &CTS_INNER_TYPE(cstr), CTS_FLAG_NONE, NULL},
    {offsetof(Student, gender), "gender", &Gender_type, CTS_FLAG_NONE, NULL},
    {offsetof(Student, active), "active", &CTS_INNER_TYPE(bool), CTS_FLAG_NONE, NULL},
};

static const cts_type student_type = {
    "Student", CTS_TYPE_STRUCT, sizeof(Student), student_fields,
    CTS_COUNT_OF(student_fields), NULL, NULL, CTS_FLAG_NONE, NULL
};

static void test_enum(void)
{
    assert(Gender_get_type() == &Gender_type);
    assert(strcmp(Gender_to_name(GENDER_MALE), "Male") == 0);
    assert(Gender_to_value("fEmAlE") == GENDER_FEMALE);
    assert(Gender_to_value("missing") == GENDER_UNKNOWN);
}

static void test_struct_json_roundtrip(void)
{
    Student input = {7, "Ada", GENDER_FEMALE, true};
    cJSON *json = cts_cJSON_serialize(&student_type, &input);
    assert(json);
    char *text = cJSON_PrintUnformatted(json);
    assert(text && strcmp(text,
        "{\"id\":7,\"name\":\"Ada\",\"gender\":\"Female\",\"active\":true}") == 0);

    Student *output = cts_cJSON_deserialize(&student_type, json);
    assert(output && output->id == 7 && strcmp(output->name, "Ada") == 0);
    assert(output->gender == GENDER_FEMALE && output->active);

    cJSON_free(text);
    cJSON_Delete(json);
    cts_free_instance(&student_type, output);
}

static void test_array_json_roundtrip(void)
{
    cts_array values;
    cts_array_init(&values, &CTS_INNER_TYPE(int32));
    int32_t one = 1, two = 2;
    assert(cts_array_push(&values, &one));
    assert(cts_array_push(&values, &two));
    cts_type array_type = cts_array_type("int32[]", &CTS_INNER_TYPE(int32));
    cJSON *json = cts_cJSON_serialize(&array_type, &values);
    char *text = cJSON_PrintUnformatted(json);
    assert(text && strcmp(text, "[1,2]") == 0);

    cts_array *copy = cts_cJSON_deserialize(&array_type, json);
    assert(copy && copy->count == 2);
    assert(*(int32_t *)cts_array_get(copy, 1) == 2);

    cJSON_free(text);
    cJSON_Delete(json);
    cts_array_dispose(&values);
    cts_free_instance(&array_type, copy);
}

int main(void)
{
    test_enum();
    test_struct_json_roundtrip();
    test_array_json_roundtrip();
    puts("all tests passed");
    return 0;
}
