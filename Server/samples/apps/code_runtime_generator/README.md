# Code-Runtime Generator Sample

This guide provides instructions on using the Ubiq-Genie framework to create an application that enables collaborative interaction with a service that generates code that will be built and loaaded within a shared environment. The code is then attached to a selected object at on the fly the behaviour generated will be available in the scene.

## Prerequisites

An Azure Speech Services subscription is needed for this sample. You can create a free subscription [here](https://azure.microsoft.com/en-us/try/cognitive-services/?api=speech-services).

Before proceeding, ensure the Ubiq-Genie framework and the necessary dependencies for this sample are correctly installed. For further details, please see the [README](../../README.md) file in the Genie folder.

## Running the Sample

Follow these steps to run the sample:

1. Open a terminal and navigate to the `Server/samples/apps/code_runtime_generator` directory. Ensure the Python virtual environment in the `samples` directory is activated (e.g., `source venv/bin/activate`).
2. Update the `key` and `serviceRegion` variables in `config.json` to match your Azure Speech Services subscription key and region.
3. Execute the following command:

    ```bash
    node app.js
    ```

4. Launch Unity and navigate to the `Unity/Assets/Samples/Ubiq-Genie/DynamicCompiler` directory. Open the `DynamicCompiler.unity` scene.
5. Check that the `Room Client` under the `Network Scene` object has the correct IP address and port for the server. If the server is running on the same machine as the Unity Editor, the IP address should be `localhost`.
6. In the Unity Editor, press the `Play` button to launch the application.
7. Speak into the microphone. The agent will respond to your queries and requests, facing you while speaking and employing simple gestures.
    - On desktop, press and hold the space bar to record a voice command, releasing it to stop the recording. In VR, use the grip button to record a voice command, releasing it to stop the recording.


## Support

For any questions or issues, please use the Discussions tab on Github or send an email to [Daniele Giunchi](mailto:d.giunchi@ucl.ac.uk).
