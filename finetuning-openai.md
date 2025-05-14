Fine-tuning

Manage fine-tuning jobs to tailor a model to your specific training data. Related guide: Fine-tune models
Create fine-tuning job
post https://api.openai.com/v1/fine_tuning/jobs

Creates a fine-tuning job which begins the process of creating a new model from a given dataset.

Response includes details of the enqueued job including job status and the name of the fine-tuned models once complete.

Learn more about fine-tuning
Request body
model

string
Required

The name of the model to fine-tune. You can select one of the supported models.
training_file

string
Required

The ID of an uploaded file that contains training data.

See upload file for how to upload a file.

Your dataset must be formatted as a JSONL file. Additionally, you must upload your file with the purpose fine-tune.

The contents of the file should differ depending on if the model uses the chat, completions format, or if the fine-tuning method uses the preference format.

See the fine-tuning guide for more details.
hyperparameters
Deprecated

object
Optional

The hyperparameters used for the fine-tuning job. This value is now deprecated in favor of method, and should be passed in under the method parameter.
integrations

array or null
Optional

A list of integrations to enable for your fine-tuning job.
metadata

map
Optional

Set of 16 key-value pairs that can be attached to an object. This can be useful for storing additional information about the object in a structured format, and querying for objects via API or the dashboard.

Keys are strings with a maximum length of 64 characters. Values are strings with a maximum length of 512 characters.
method

object
Optional

The method used for fine-tuning.
seed

integer or null
Optional

The seed controls the reproducibility of the job. Passing in the same seed and job parameters should produce the same results, but may differ in rare cases. If a seed is not specified, one will be generated for you.
suffix

string or null
Optional
Defaults to null

A string of up to 64 characters that will be added to your fine-tuned model name.

For example, a suffix of "custom-model-name" would produce a model name like ft:gpt-4o-mini:openai:custom-model-name:7p4lURel.
validation_file

string or null
Optional

The ID of an uploaded file that contains validation data.

If you provide this file, the data is used to generate validation metrics periodically during fine-tuning. These metrics can be viewed in the fine-tuning results file. The same data should not be present in both train and validation files.

Your dataset must be formatted as a JSONL file. You must upload your file with the purpose fine-tune.

See the fine-tuning guide for more details.
Returns

A fine-tuning.job object.
Example request

curl https://api.openai.com/v1/fine_tuning/jobs \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "training_file": "file-BK7bzQj3FfZFXr7DbL6xJwfo",
    "model": "gpt-4o-mini"
  }'

Response

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "gpt-4o-mini-2024-07-18",
  "created_at": 1721764800,
  "fine_tuned_model": null,
  "organization_id": "org-123",
  "result_files": [],
  "status": "queued",
  "validation_file": null,
  "training_file": "file-abc123",
  "method": {
    "type": "supervised",
    "supervised": {
      "hyperparameters": {
        "batch_size": "auto",
        "learning_rate_multiplier": "auto",
        "n_epochs": "auto",
      }
    }
  },
  "metadata": null
}

List fine-tuning jobs
get https://api.openai.com/v1/fine_tuning/jobs

List your organization's fine-tuning jobs
Query parameters
after

string
Optional

Identifier for the last job from the previous pagination request.
limit

integer
Optional
Defaults to 20

Number of fine-tuning jobs to retrieve.
metadata

object or null
Optional

Optional metadata filter. To filter, use the syntax metadata[k]=v. Alternatively, set metadata=null to indicate no metadata.
Returns

A list of paginated fine-tuning job objects.
Example request

curl https://api.openai.com/v1/fine_tuning/jobs?limit=2&metadata[key]=value \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "list",
  "data": [
    {
      "object": "fine_tuning.job",
      "id": "ftjob-abc123",
      "model": "gpt-4o-mini-2024-07-18",
      "created_at": 1721764800,
      "fine_tuned_model": null,
      "organization_id": "org-123",
      "result_files": [],
      "status": "queued",
      "validation_file": null,
      "training_file": "file-abc123",
      "metadata": {
        "key": "value"
      }
    },
    { ... },
    { ... }
  ], "has_more": true
}

List fine-tuning events
get https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}/events

Get status updates for a fine-tuning job.
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job to get events for.
Query parameters
after

string
Optional

Identifier for the last event from the previous pagination request.
limit

integer
Optional
Defaults to 20

Number of events to retrieve.
Returns

A list of fine-tuning event objects.
Example request

curl https://api.openai.com/v1/fine_tuning/jobs/ftjob-abc123/events \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "list",
  "data": [
    {
      "object": "fine_tuning.job.event",
      "id": "ft-event-ddTJfwuMVpfLXseO0Am0Gqjm",
      "created_at": 1721764800,
      "level": "info",
      "message": "Fine tuning job successfully completed",
      "data": null,
      "type": "message"
    },
    {
      "object": "fine_tuning.job.event",
      "id": "ft-event-tyiGuB72evQncpH87xe505Sv",
      "created_at": 1721764800,
      "level": "info",
      "message": "New fine-tuned model created: ft:gpt-4o-mini:openai::7p4lURel",
      "data": null,
      "type": "message"
    }
  ],
  "has_more": true
}

List fine-tuning checkpoints
get https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}/checkpoints

List checkpoints for a fine-tuning job.
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job to get checkpoints for.
Query parameters
after

string
Optional

Identifier for the last checkpoint ID from the previous pagination request.
limit

integer
Optional
Defaults to 10

Number of checkpoints to retrieve.
Returns

A list of fine-tuning checkpoint objects for a fine-tuning job.
Example request

curl https://api.openai.com/v1/fine_tuning/jobs/ftjob-abc123/checkpoints \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "list"
  "data": [
    {
      "object": "fine_tuning.job.checkpoint",
      "id": "ftckpt_zc4Q7MP6XxulcVzj4MZdwsAB",
      "created_at": 1721764867,
      "fine_tuned_model_checkpoint": "ft:gpt-4o-mini-2024-07-18:my-org:custom-suffix:96olL566:ckpt-step-2000",
      "metrics": {
        "full_valid_loss": 0.134,
        "full_valid_mean_token_accuracy": 0.874
      },
      "fine_tuning_job_id": "ftjob-abc123",
      "step_number": 2000,
    },
    {
      "object": "fine_tuning.job.checkpoint",
      "id": "ftckpt_enQCFmOTGj3syEpYVhBRLTSy",
      "created_at": 1721764800,
      "fine_tuned_model_checkpoint": "ft:gpt-4o-mini-2024-07-18:my-org:custom-suffix:7q8mpxmy:ckpt-step-1000",
      "metrics": {
        "full_valid_loss": 0.167,
        "full_valid_mean_token_accuracy": 0.781
      },
      "fine_tuning_job_id": "ftjob-abc123",
      "step_number": 1000,
    },
  ],
  "first_id": "ftckpt_zc4Q7MP6XxulcVzj4MZdwsAB",
  "last_id": "ftckpt_enQCFmOTGj3syEpYVhBRLTSy",
  "has_more": true
}

List checkpoint permissions
get https://api.openai.com/v1/fine_tuning/checkpoints/{fine_tuned_model_checkpoint}/permissions

NOTE: This endpoint requires an admin API key.

Organization owners can use this endpoint to view all permissions for a fine-tuned model checkpoint.
Path parameters
fine_tuned_model_checkpoint

string
Required

The ID of the fine-tuned model checkpoint to get permissions for.
Query parameters
after

string
Optional

Identifier for the last permission ID from the previous pagination request.
limit

integer
Optional
Defaults to 10

Number of permissions to retrieve.
order

string
Optional
Defaults to descending

The order in which to retrieve permissions.
project_id

string
Optional

The ID of the project to get permissions for.
Returns

A list of fine-tuned model checkpoint permission objects for a fine-tuned model checkpoint.
Example request

curl https://api.openai.com/v1/fine_tuning/checkpoints/ft:gpt-4o-mini-2024-07-18:org:weather:B7R9VjQd/permissions \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "list",
  "data": [
    {
      "object": "checkpoint.permission",
      "id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
      "created_at": 1721764867,
      "project_id": "proj_abGMw1llN8IrBb6SvvY5A1iH"
    },
    {
      "object": "checkpoint.permission",
      "id": "cp_enQCFmOTGj3syEpYVhBRLTSy",
      "created_at": 1721764800,
      "project_id": "proj_iqGMw1llN8IrBb6SvvY5A1oF"
    },
  ],
  "first_id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
  "last_id": "cp_enQCFmOTGj3syEpYVhBRLTSy",
  "has_more": false
}

Create checkpoint permissions
post https://api.openai.com/v1/fine_tuning/checkpoints/{fine_tuned_model_checkpoint}/permissions

NOTE: Calling this endpoint requires an admin API key.

This enables organization owners to share fine-tuned models with other projects in their organization.
Path parameters
fine_tuned_model_checkpoint

string
Required

The ID of the fine-tuned model checkpoint to create a permission for.
Request body
project_ids

array
Required

The project identifiers to grant access to.
Returns

A list of fine-tuned model checkpoint permission objects for a fine-tuned model checkpoint.
Example request

curl https://api.openai.com/v1/fine_tuning/checkpoints/ft:gpt-4o-mini-2024-07-18:org:weather:B7R9VjQd/permissions \
  -H "Authorization: Bearer $OPENAI_API_KEY"
  -d '{"project_ids": ["proj_abGMw1llN8IrBb6SvvY5A1iH"]}'

Response

{
  "object": "list",
  "data": [
    {
      "object": "checkpoint.permission",
      "id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
      "created_at": 1721764867,
      "project_id": "proj_abGMw1llN8IrBb6SvvY5A1iH"
    }
  ],
  "first_id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
  "last_id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
  "has_more": false
}

Delete checkpoint permission
delete https://api.openai.com/v1/fine_tuning/checkpoints/{fine_tuned_model_checkpoint}/permissions/{permission_id}

NOTE: This endpoint requires an admin API key.

Organization owners can use this endpoint to delete a permission for a fine-tuned model checkpoint.
Path parameters
fine_tuned_model_checkpoint

string
Required

The ID of the fine-tuned model checkpoint to delete a permission for.
permission_id

string
Required

The ID of the fine-tuned model checkpoint permission to delete.
Returns

The deletion status of the fine-tuned model checkpoint permission object.
Example request

curl https://api.openai.com/v1/fine_tuning/checkpoints/ft:gpt-4o-mini-2024-07-18:org:weather:B7R9VjQd/permissions/cp_zc4Q7MP6XxulcVzj4MZdwsAB \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "checkpoint.permission",
  "id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
  "deleted": true
}

Retrieve fine-tuning job
get https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}

Get info about a fine-tuning job.

Learn more about fine-tuning
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job.
Returns

The fine-tuning object with the given ID.
Example request

curl https://api.openai.com/v1/fine_tuning/jobs/ft-AF1WoRqd3aJAHsqc9NY7iL8F \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "davinci-002",
  "created_at": 1692661014,
  "finished_at": 1692661190,
  "fine_tuned_model": "ft:davinci-002:my-org:custom_suffix:7q8mpxmy",
  "organization_id": "org-123",
  "result_files": [
      "file-abc123"
  ],
  "status": "succeeded",
  "validation_file": null,
  "training_file": "file-abc123",
  "hyperparameters": {
      "n_epochs": 4,
      "batch_size": 1,
      "learning_rate_multiplier": 1.0
  },
  "trained_tokens": 5768,
  "integrations": [],
  "seed": 0,
  "estimated_finish": 0,
  "method": {
    "type": "supervised",
    "supervised": {
      "hyperparameters": {
        "n_epochs": 4,
        "batch_size": 1,
        "learning_rate_multiplier": 1.0
      }
    }
  }
}

Cancel fine-tuning
post https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}/cancel

Immediately cancel a fine-tune job.
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job to cancel.
Returns

The cancelled fine-tuning object.
Example request

curl -X POST https://api.openai.com/v1/fine_tuning/jobs/ftjob-abc123/cancel \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "gpt-4o-mini-2024-07-18",
  "created_at": 1721764800,
  "fine_tuned_model": null,
  "organization_id": "org-123",
  "result_files": [],
  "status": "cancelled",
  "validation_file": "file-abc123",
  "training_file": "file-abc123"
}

Resume fine-tuning
post https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}/resume

Resume a fine-tune job.
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job to resume.
Returns

The resumed fine-tuning object.
Example request

curl -X POST https://api.openai.com/v1/fine_tuning/jobs/ftjob-abc123/resume \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "gpt-4o-mini-2024-07-18",
  "created_at": 1721764800,
  "fine_tuned_model": null,
  "organization_id": "org-123",
  "result_files": [],
  "status": "queued",
  "validation_file": "file-abc123",
  "training_file": "file-abc123"
}

Pause fine-tuning
post https://api.openai.com/v1/fine_tuning/jobs/{fine_tuning_job_id}/pause

Pause a fine-tune job.
Path parameters
fine_tuning_job_id

string
Required

The ID of the fine-tuning job to pause.
Returns

The paused fine-tuning object.
Example request

curl -X POST https://api.openai.com/v1/fine_tuning/jobs/ftjob-abc123/pause \
  -H "Authorization: Bearer $OPENAI_API_KEY"

Response

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "gpt-4o-mini-2024-07-18",
  "created_at": 1721764800,
  "fine_tuned_model": null,
  "organization_id": "org-123",
  "result_files": [],
  "status": "paused",
  "validation_file": "file-abc123",
  "training_file": "file-abc123"
}

Training format for chat models using the supervised method

The per-line training example of a fine-tuning input file for chat models using the supervised method.
functions
Deprecated

array

A list of functions the model may generate JSON inputs for.
messages

array
parallel_tool_calls

boolean

Whether to enable parallel function calling during tool use.
tools

array

A list of tools the model may generate JSON inputs for.
OBJECT Training format for chat models using the supervised method

{
  "messages": [
    { "role": "user", "content": "What is the weather in San Francisco?" },
    {
      "role": "assistant",
      "tool_calls": [
        {
          "id": "call_id",
          "type": "function",
          "function": {
            "name": "get_current_weather",
            "arguments": "{\"location\": \"San Francisco, USA\", \"format\": \"celsius\"}"
          }
        }
      ]
    }
  ],
  "parallel_tool_calls": false,
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_current_weather",
        "description": "Get the current weather",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {
                "type": "string",
                "description": "The city and country, eg. San Francisco, USA"
            },
            "format": { "type": "string", "enum": ["celsius", "fahrenheit"] }
          },
          "required": ["location", "format"]
        }
      }
    }
  ]
}

Training format for chat models using the preference method

The per-line training example of a fine-tuning input file for chat models using the dpo method.
input

object
non_preferred_completion

array

The non-preferred completion message for the output.
preferred_completion

array

The preferred completion message for the output.
OBJECT Training format for chat models using the preference method

{
  "input": {
    "messages": [
      { "role": "user", "content": "What is the weather in San Francisco?" }
    ]
  },
  "preferred_completion": [
    {
      "role": "assistant",
      "content": "The weather in San Francisco is 70 degrees Fahrenheit."
    }
  ],
  "non_preferred_completion": [
    {
      "role": "assistant",
      "content": "The weather in San Francisco is 21 degrees Celsius."
    }
  ]
}

Training format for reasoning models using the reinforcement method

Per-line training example for reinforcement fine-tuning. Note that messages and tools are the only reserved keywords. Any other arbitrary key-value data can be included on training datapoints and will be available to reference during grading under the {{ item.XXX }} template variable.
messages

array
tools

array

A list of tools the model may generate JSON inputs for.
OBJECT Training format for reasoning models using the reinforcement method

{
  "messages": [
    {
      "role": "user",
      "content": "Your task is to take a chemical in SMILES format and predict the number of hydrobond bond donors and acceptors according to Lipinkski's rule. CCN(CC)CCC(=O)c1sc(N)nc1C"
    },
  ],
  # Any other JSON data can be inserted into an example and referenced during RFT grading
  "reference_answer": {
    "donor_bond_counts": 5,
    "acceptor_bond_counts": 7
  }
}

The fine-tuning job object

The fine_tuning.job object represents a fine-tuning job that has been created through the API.
created_at

integer

The Unix timestamp (in seconds) for when the fine-tuning job was created.
error

object or null

For fine-tuning jobs that have failed, this will contain more information on the cause of the failure.
estimated_finish

integer or null

The Unix timestamp (in seconds) for when the fine-tuning job is estimated to finish. The value will be null if the fine-tuning job is not running.
fine_tuned_model

string or null

The name of the fine-tuned model that is being created. The value will be null if the fine-tuning job is still running.
finished_at

integer or null

The Unix timestamp (in seconds) for when the fine-tuning job was finished. The value will be null if the fine-tuning job is still running.
hyperparameters

object

The hyperparameters used for the fine-tuning job. This value will only be returned when running supervised jobs.
id

string

The object identifier, which can be referenced in the API endpoints.
integrations

array or null

A list of integrations to enable for this fine-tuning job.
metadata

map

Set of 16 key-value pairs that can be attached to an object. This can be useful for storing additional information about the object in a structured format, and querying for objects via API or the dashboard.

Keys are strings with a maximum length of 64 characters. Values are strings with a maximum length of 512 characters.
method

object

The method used for fine-tuning.
model

string

The base model that is being fine-tuned.
object

string

The object type, which is always "fine_tuning.job".
organization_id

string

The organization that owns the fine-tuning job.
result_files

array

The compiled results file ID(s) for the fine-tuning job. You can retrieve the results with the Files API.
seed

integer

The seed used for the fine-tuning job.
status

string

The current status of the fine-tuning job, which can be either validating_files, queued, running, succeeded, failed, or cancelled.
trained_tokens

integer or null

The total number of billable tokens processed by this fine-tuning job. The value will be null if the fine-tuning job is still running.
training_file

string

The file ID used for training. You can retrieve the training data with the Files API.
validation_file

string or null

The file ID used for validation. You can retrieve the validation results with the Files API.
OBJECT The fine-tuning job object

{
  "object": "fine_tuning.job",
  "id": "ftjob-abc123",
  "model": "davinci-002",
  "created_at": 1692661014,
  "finished_at": 1692661190,
  "fine_tuned_model": "ft:davinci-002:my-org:custom_suffix:7q8mpxmy",
  "organization_id": "org-123",
  "result_files": [
      "file-abc123"
  ],
  "status": "succeeded",
  "validation_file": null,
  "training_file": "file-abc123",
  "hyperparameters": {
      "n_epochs": 4,
      "batch_size": 1,
      "learning_rate_multiplier": 1.0
  },
  "trained_tokens": 5768,
  "integrations": [],
  "seed": 0,
  "estimated_finish": 0,
  "method": {
    "type": "supervised",
    "supervised": {
      "hyperparameters": {
        "n_epochs": 4,
        "batch_size": 1,
        "learning_rate_multiplier": 1.0
      }
    }
  },
  "metadata": {
    "key": "value"
  }
}

The fine-tuning job event object

Fine-tuning job event object
created_at

integer

The Unix timestamp (in seconds) for when the fine-tuning job was created.
data

object

The data associated with the event.
id

string

The object identifier.
level

string

The log level of the event.
message

string

The message of the event.
object

string

The object type, which is always "fine_tuning.job.event".
type

string

The type of event.
OBJECT The fine-tuning job event object

{
  "object": "fine_tuning.job.event",
  "id": "ftevent-abc123"
  "created_at": 1677610602,
  "level": "info",
  "message": "Created fine-tuning job",
  "data": {},
  "type": "message"
}

The fine-tuning job checkpoint object

The fine_tuning.job.checkpoint object represents a model checkpoint for a fine-tuning job that is ready to use.
created_at

integer

The Unix timestamp (in seconds) for when the checkpoint was created.
fine_tuned_model_checkpoint

string

The name of the fine-tuned checkpoint model that is created.
fine_tuning_job_id

string

The name of the fine-tuning job that this checkpoint was created from.
id

string

The checkpoint identifier, which can be referenced in the API endpoints.
metrics

object

Metrics at the step number during the fine-tuning job.
object

string

The object type, which is always "fine_tuning.job.checkpoint".
step_number

integer

The step number that the checkpoint was created at.
OBJECT The fine-tuning job checkpoint object

{
  "object": "fine_tuning.job.checkpoint",
  "id": "ftckpt_qtZ5Gyk4BLq1SfLFWp3RtO3P",
  "created_at": 1712211699,
  "fine_tuned_model_checkpoint": "ft:gpt-4o-mini-2024-07-18:my-org:custom_suffix:9ABel2dg:ckpt-step-88",
  "fine_tuning_job_id": "ftjob-fpbNQ3H1GrMehXRf8cO97xTN",
  "metrics": {
    "step": 88,
    "train_loss": 0.478,
    "train_mean_token_accuracy": 0.924,
    "valid_loss": 10.112,
    "valid_mean_token_accuracy": 0.145,
    "full_valid_loss": 0.567,
    "full_valid_mean_token_accuracy": 0.944
  },
  "step_number": 88
}

The fine-tuned model checkpoint permission object

The checkpoint.permission object represents a permission for a fine-tuned model checkpoint.
created_at

integer

The Unix timestamp (in seconds) for when the permission was created.
id

string

The permission identifier, which can be referenced in the API endpoints.
object

string

The object type, which is always "checkpoint.permission".
project_id

string

The project identifier that the permission is for.
OBJECT The fine-tuned model checkpoint permission object

{
  "object": "checkpoint.permission",
  "id": "cp_zc4Q7MP6XxulcVzj4MZdwsAB",
  "created_at": 1712211699,
  "project_id": "proj_abGMw1llN8IrBb6SvvY5A1iH"
}