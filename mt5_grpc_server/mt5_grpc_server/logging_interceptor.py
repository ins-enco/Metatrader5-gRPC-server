import grpc
import logging
import json
from google.protobuf.json_format import MessageToDict

SENSITIVE_FIELD_NAMES = {
    "password",
    "passwd",
    "pwd",
    "token",
    "access_token",
    "refresh_token",
    "secret",
    "api_key",
    "apikey",
}
REDACTED_VALUE = "[REDACTED]"


class VerboseLoggingInterceptor(grpc.ServerInterceptor):
    """
    A gRPC server interceptor that provides verbose logging of all requests and responses.
    
    This interceptor logs:
    - All incoming requests with their parameters
    - All outgoing responses with their data
    - Any errors that occur during request processing
    - Supports both unary and streaming methods
    """

    def __init__(self, logger=None):
        """
        Initialize the interceptor with a logger.
        
        Args:
            logger: Optional logger instance. If not provided, will create a new one.
        """
        self._logger = logger or logging.getLogger('mt5_grpc_server')

    def _message_to_dict(self, message):
        """Convert a protobuf message to a dict, with error handling."""
        try:
            if hasattr(message, 'test_field'):  # Handle test messages
                return self._redact_sensitive_fields({'test_field': message.test_field})
            return self._redact_sensitive_fields(
                MessageToDict(message, preserving_proto_field_name=True)
            )
        except Exception as e:
            self._logger.warning(f"Failed to convert message to dict: {e}")
            return {"__str__": str(message)}

    def _redact_sensitive_fields(self, value):
        """Redact secret-like fields before request/response payload logging."""
        if isinstance(value, dict):
            return {
                key: REDACTED_VALUE
                if key.lower() in SENSITIVE_FIELD_NAMES
                else self._redact_sensitive_fields(item)
                for key, item in value.items()
            }
        if isinstance(value, list):
            return [self._redact_sensitive_fields(item) for item in value]
        return value

    def intercept_service(self, continuation, handler_call_details):
        """
        Intercept and log gRPC service calls.
        
        Args:
            continuation: Function to continue the RPC call
            handler_call_details: Details about the RPC call
            
        Returns:
            A wrapped RPC method handler that includes logging
        """
        # Get method name from the handler details
        method_name = handler_call_details.method.split('/')[-1]
        
        def logging_wrapper(behavior, request_streaming, response_streaming):
            def wrapper(request_or_iterator, context):
                # Log request
                if request_streaming:
                    requests = []
                    for request in request_or_iterator:
                        request_dict = self._message_to_dict(request)
                        requests.append(request_dict)
                        self._logger.info(f"Request for {method_name}: {json.dumps(requests, indent=2)}")
                else:
                    request_dict = self._message_to_dict(request_or_iterator)
                    self._logger.info(f"Request for {method_name}: {json.dumps(request_dict, indent=2)}")

                # Call the actual handler
                try:
                    response_or_iterator = behavior(request_or_iterator, context)

                    # Log response
                    if response_streaming:
                        def response_generator():
                            for response in response_or_iterator:
                                response_dict = self._message_to_dict(response)
                                self._logger.info(f"Response from {method_name}: {json.dumps(response_dict, indent=2)}")
                                yield response
                        return response_generator()
                    else:
                        response_dict = self._message_to_dict(response_or_iterator)
                        self._logger.info(f"Response from {method_name}: {json.dumps(response_dict, indent=2)}")
                        return response_or_iterator
                except Exception as e:
                    self._logger.error(f"Error in {method_name}: {str(e)}")
                    raise

            return wrapper

        handler = continuation(handler_call_details)
        if handler is None:
            return handler

        # Check if handler has the required attributes
        if hasattr(handler, 'request_streaming') and hasattr(handler, 'response_streaming'):
            # Create a new handler with the same properties but wrapped methods
            class WrappedRpcMethodHandler:
                def __init__(self):
                    self.request_streaming = handler.request_streaming
                    self.response_streaming = handler.response_streaming
                    self.request_deserializer = handler.request_deserializer
                    self.response_serializer = handler.response_serializer
                    self.unary_unary = logging_wrapper(handler.unary_unary, False, False) if handler.unary_unary else None
                    self.unary_stream = logging_wrapper(handler.unary_stream, False, True) if handler.unary_stream else None
                    self.stream_unary = logging_wrapper(handler.stream_unary, True, False) if handler.stream_unary else None
                    self.stream_stream = logging_wrapper(handler.stream_stream, True, True) if handler.stream_stream else None

            return WrappedRpcMethodHandler()
        return handler 
