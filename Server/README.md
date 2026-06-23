# Ubiq-Genie

Ubiq-Genie is a framework that enables you to build server-assisted collaborative mixed reality applications with Unity using the [Ubiq](https://ubiq.online) framework.

## Local Setup

The `Server` folder is not runnable with `npm install` alone.

This repository is now trimmed for the `samples/apps/code_runtime_generator` flow first. The default Python requirements only include the package needed by that sample's OpenAI worker.

The Node side depends on:

- a vendored copy of the historical UCL-VR `Node` package under `Server/vendor/ubiq`
- top-level Node packages such as `nconf`
- a Python virtual environment for the sample services
- runtime environment variables such as `OPENAI_API_KEY`

Because of this, local setup should be treated as a two-step process: install dependencies, then validate the environment before starting a sample app.

### 1. Install Node dependencies

From the `Server` folder run:

```powershell
npm install
```

Notes:

- The repository no longer depends on `gitpkg.now.sh` for `ubiq`; it now uses the vendored package in `Server/vendor/ubiq`.
- The server bootstrap in `components/application.js` still expects `node_modules/ubiq/app.js` to exist locally after `npm install`.

### 2. Run the local setup check

Use the helper script below to verify the pieces that `npm install` does not guarantee by itself:

```powershell
npm run doctor
```

The check reports whether the local machine can resolve:

- `ubiq`
- `nconf`
- the Python virtual environment under `samples/venv`
- `OPENAI_API_KEY`
- `STT_HTTP_URL`

### 3. Create the Python virtual environment

For `code_runtime_generator`, create the environment from `Server/samples`:

```powershell
cd samples
py -3.10 -m venv .\venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip setuptools wheel
pip install -r requirements.txt
```

`samples/requirements.txt` is intentionally minimal and currently installs only `openai==0.28.1`, which is what `samples/services/code_generation/openai_chatgpt_api.py` needs.

If you later need the older broad dependency set for image generation, local Whisper, or other legacy samples, it has been preserved in:

```text
samples/requirements-legacy-all.txt
```

### 4. Set runtime environment variables

For the DreamCodeVR code generation sample:

```powershell
$env:OPENAI_API_KEY="sk-proj-your-real-key"
$env:OPENAI_MODEL="gpt-5.5"
$env:STT_HTTP_URL="http://130.136.2.161:50101/stt/transcribe"
```

`STT_HTTP_URL` is especially important for local runs. The code currently falls back to the remote endpoint above, which may be down or inaccessible from your network.

### 5. Start a sample app

From the `Server` folder you can start the main sample used in this repository with:

```powershell
npm run start:code-runtime-generator
```

The script above changes into the correct sample directory before bootstrapping the app, so `config.json`, `cert.pem`, and `key.pem` are resolved correctly.

Equivalent manual command:

```powershell
cd samples\apps\code_runtime_generator
node app.js
```

## Known Local Setup Failure Modes

### `npm install` does not materialize `node_modules/ubiq`

The `ubiq` dependency in `package.json` is now resolved from the vendored folder:

```text
file:vendor/ubiq
```

If the install is interrupted or `node_modules` is stale, the app will still fail on imports such as:

```javascript
require("ubiq/ubiq/messaging")
```

and the room server bootstrap in `components/application.js` will also fail because `node_modules/ubiq/app.js` is missing.

### `nconf` is missing

The project code imports `nconf` directly from the sample apps and from `components/application.js`, so it must be installed at the top level of `Server/node_modules`.

### Python services do not start

If `samples/venv` does not exist, services such as code generation fall back to a plain `python` executable and may fail depending on your PATH and installed packages.

## What `npm install` Does Not Do

`npm install` does not:

- create `samples/venv`
- install `samples/requirements.txt`
- set `OPENAI_API_KEY`
- ensure that the remote STT endpoint is reachable
- validate that the vendored `ubiq` package has been linked into `node_modules`

## Python Scope

The default Python environment is now scoped to `code_runtime_generator`.

- `samples/requirements.txt` is the minimal install path for code generation.
- `samples/requirements-legacy-all.txt` preserves the older cross-sample dependency set.

## Vendored Ubiq Source

To keep this project compatible with the existing CommonJS imports such as `require("ubiq/ubiq/messaging")`, the repository vendors the historical `Node` package from the UCL-VR `ubiq` repository at commit `176b628c1af34aedad19a35ed5bf4c5a8473953e`.

The vendored source lives in:

```text
Server/vendor/ubiq
```

Runtime data and local dependencies are intentionally ignored by Git: `node_modules`, `samples/venv`, Python `__pycache__`, and sample `data/input.txt` / `data/response.txt`.

Ubiq-Genie has a modular architecture designed to facilitate the integration of new services and the ability to update or replace individual services without affecting the entire system. The architecture consists of three main components: the Unity scene, applications, and services.

## System Architecture

-   **Unity Scenes** serve as the interface for VR users and contains application-specific Unity components that communicate with a server-side `ApplicationController` through a TCP connection, using either Ubiq's `Networking` or `Logging` components. These client-side components are written in C# and ensure that outgoing and incoming data are processed and routed correctly.

-   **Applications** should have an associated Unite scene and `ApplicationController`. The `ApplicationController` is responsible for initialising and managing the services that are required by the application. It also handles the communication between the services and the Unity scene. The `ApplicationController` is written in Node.js and runs on the server. The `ApplicationController` of each of the sample applications can be found in the `app.js` file in the corresponding folder in the `Server/samples/apps` folder.

-   **Services** are modular and can be reused in different applications. Each service is responsible for a specific task and is managed by a `ServiceController`. Services typically use child processes to run external applications. For instance, the `ImageGenerationService` spawns a Python child process to generate images with Stable Diffusion 2.0. The `ServiceController` is written in Node.js and runs on the server. The `ServiceController` of each of the sample services can be found in the `service.js` file in the corresponding folder in the `Server/samples/services` folder.

## Defining New Services

To define a new service, follow these steps:

1. To define a new service, create a new folder in the `Server/samples/services` folder with the name of your service.

2. Create a new file in the folder you just created called `service.js`. This file will contain the `ServiceController` of your service. A minimal example of a `ServiceController` is shown below:

    ```javascript
    const { Service } = require("../../components/service");

    class ExampleService extends Service {
        constructor(scene, config = {}) {
            super(scene, "ExampleService", config);

            this.registerChildProcess("default", "python", [
                "-u",
                "../../services/example_service/example_service.py"
                "--example_arg",
                config.example_arg
            ]);
        }
    }

    module.exports = {
        ExampleService
    };
    ```

3. For any child processes that your services requires (e.g., Python scripts), copy the corresponding files into the folder you just created. For instance, if your service requires a Python script called `example_service.py`, copy this file into the folder you just created.

4. Add the following line to the `Server/samples/services/index.js` file:

    ```javascript
    const { ExampleService } = require("./example_service/service");
    ```

    This line will ensure that the `ExampleService` class is exported when the `Server/samples/services/index.js` file is imported.

You are now ready to use your new service in an application. For more information on how to define a new application, see the `How to Define a New Application` section below.

## Defining New Applications

To define a new application, follow these steps:

1. To define a new application, create a new folder in the `Server/samples/apps` folder with the name of your application (e.g., `my_app`). This folder will contains all the files required by your application.

2. Create a new file in the folder you just created called `app.js`. This file will contain the `ApplicationController` of your application. A minimal example of an `ApplicationController` is shown below:

    ```javascript
    const { MessageReader, ApplicationController } = require("ubiq-genie-components");
    const { ExampleService } = require("ubiq-genie-services");
    const fs = require("fs");
    const nconf = require("nconf");

    class ExampleApplication extends ApplicationController {
        constructor(configFile = "config.json") {
            super(configFile);
        }

        registerComponents() {
            // A MessageReader to read messages based on fixed network ID
            this.components.messageReceiver = new MessageReader(this.scene, 98);

            // An ExampleService to process the messages
            this.components.exampleService = new ExampleService(this.scene, nconf.get());

            // A file writer to write the output to a file
            this.components.writer = fs.createWriteStream("output.txt");
        }

        definePipeline() {
            // Step 1: When we receive a message, send it to the example service
            this.components.messageReceiver.on("data", (data) => {
                this.components.exampleService.sendToChildProcess(data.toString() + "\n");
            });

            // Step 2: When we receive a response from the example service, write it to the file
            this.components.speech2text.on("response", (data, identifier) => {
                this.components.writer.write(identifier + ": " + data.toString().substring(1));
            });

            // Step 3: In addition, send the response to the Unity scene based on a fixed network ID
            this.components.speech2text.on("response", (data, identifier) => {
                this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
                    type: "ExampleApplication",
                    data: data,
                });
            });
        }
    }

    module.exports = { ExampleApplication };

    if (require.main === module) {
        const app = new ExampleApplication();
        app.start();
    }
    ```

    This example application uses the `ExampleService` service that we defined in the previous section. The `ExampleApplication` class extends the `ApplicationController` class and defines the components and pipeline of the application. The `registerComponents` method defines the components of the application, which are stored in a dictionary called `components`. The `definePipeline` method defines the pipeline of the application. The `registerComponents` and `definePipeline` methods are called by `start` method of the `ApplicationController` class.

3. Create a new file in the folder you just created called `config.json`. This file will contain the configuration of your application. For more information on how to define a configuration file, see the `Configuration File` section below. A minimal example of a configuration file is shown below:

    ```json
    {
        "name": "ExampleApplication",
        "roomGuid": "3b8b5f0c-5b9a-4b9a-9c1a-3b8b5f0c5b9a",
        "outputNetworkId": 99,
        "roomserver": {
            "tcp": {
                "port": 8009
            },
            "wss": {
                "port": 8010,
                "cert": "./cert.pem",
                "key": "./key.pem"
            }
        },
        "iceservers": [
            {
                "uri": "stun:stun.l.google.com:19302"
            }
        ]
    }
    ```

    This includes the name of the application, the GUID of the room that the application will join, the information required to start a Ubiq server, and a fixed network ID that is used to send messages to the Unity scene. For more information on Ubiq servers and messages, see the [Ubiq documentation](https://ucl-vr.github.io/ubiq/serverintroduction/).

4. In Unity, create a new scene and set it up for Ubiq. For more information on how to set up a scene for Ubiq, see the [Ubiq documentation](https://ucl-vr.github.io/ubiq/unityintroduction/). We recommend to use the `StartHere` scene as a starting point.

5. In your newly created Unity scene, add a new empty GameObject. To this GameObject, add a new script called with a name of your choice (e.g., `ExampleApplication`). This script will contain the client-side Unity counterpart of your application. A minimal example of a Unity script is shown below:

    ```csharp
    using System;
    using UnityEngine;
    using Ubiq.Networking;
    using Ubiq.Dictionaries;
    using Ubiq.Messaging;

    public class ExampleApplication : MonoBehaviour
    {
        public NetworkId networkId = new NetworkId(99);
        private NetworkContext context;

        [Serializable]
        private struct Message
        {
            public string type;
            public string data;
        }

        void Start()
        {
            context = NetworkScene.Register(this, networkId);
        }

        void Update()
        {

        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
        {
            Message message = data.FromJson<Message>();
            Debug.Log(message.data);
        }
    }

    ```

    This script registers the `ExampleApplication` class with the Ubiq network with a fixed network ID (corresponding to the network ID we use in the server-side `ApplicationController`). It also defines a `ProcessMessage` method that is called when a message is received from the Ubiq network. Whenever a message is received, the `ProcessMessage` method is called with the message as an argument. This allows the `ExampleApplication` class to process the message and perform any required actions (e.g., display the received data).
