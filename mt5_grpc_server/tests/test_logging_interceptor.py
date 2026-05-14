import unittest
import grpc
import logging
import sys
from unittest.mock import MagicMock, patch
from google.protobuf.message import Message

# Mock the MetaTrader5 import
sys.modules['MetaTrader5'] = MagicMock()

# Import the logging interceptor directly
import mt5_grpc_server.logging_interceptor
from mt5_grpc_server.logging_interceptor import VerboseLoggingInterceptor

class TestMessage:
    """Mock protobuf-like message for testing"""
    def __init__(self):
        self.test_field = "test_value"

    def __str__(self):
        return f"TestMessage(test_field={self.test_field})"

    def __eq__(self, other):
        if not isinstance(other, TestMessage):
            return False
        return self.test_field == other.test_field

class SecretMessage:
    """Mock protobuf-like message containing secret fields for testing"""
    def __init__(self):
        self.password = "plain-text-password"
        self.token = "plain-text-token"

class MockRpcMethodHandler:
    """Mock RPC method handler that properly implements the interface"""
    def __init__(self, behavior, request_streaming=False, response_streaming=False):
        self.request_streaming = request_streaming
        self.response_streaming = response_streaming
        self.request_deserializer = None
        self.response_serializer = None
        self._behavior = behavior
        self.unary_unary = self._wrap_behavior() if not (request_streaming or response_streaming) else None
        self.unary_stream = self._wrap_behavior() if not request_streaming and response_streaming else None
        self.stream_unary = self._wrap_behavior() if request_streaming and not response_streaming else None
        self.stream_stream = self._wrap_behavior() if request_streaming and response_streaming else None

    def _wrap_behavior(self):
        def wrapped(*args, **kwargs):
            try:
                return self._behavior(*args, **kwargs)
            except Exception as e:
                raise e
        return wrapped

class TestVerboseLoggingInterceptor(unittest.TestCase):
    def setUp(self):
        # Create a logger mock
        self.logger_mock = MagicMock()
        
        # Create the interceptor with our mock logger
        self.interceptor = VerboseLoggingInterceptor(logger=self.logger_mock)
        self.context = MagicMock()
        
        # Create a mock handler details
        self.handler_details = MagicMock()
        self.handler_details.method = "/test.Service/TestMethod"

    def create_mock_handler(self, behavior, request_streaming=False, response_streaming=False):
        """Helper method to create a mock RPC method handler"""
        return MockRpcMethodHandler(behavior, request_streaming, response_streaming)

    def test_unary_unary_logging(self):
        # Create mock request and response
        request = TestMessage()
        response = TestMessage()
        
        # Create mock behavior that returns our test response
        behavior = MagicMock(return_value=response)
        
        # Create mock handler
        handler = self.create_mock_handler(behavior)
        
        # Create continuation that returns our handler
        continuation = MagicMock(return_value=handler)
        
        # Get wrapped handler
        wrapped_handler = self.interceptor.intercept_service(continuation, self.handler_details)
        
        # Call the wrapped unary_unary
        result = wrapped_handler.unary_unary(request, self.context)
        
        # Verify the result
        self.assertEqual(result, response)
        
        # Verify logging calls
        self.assertEqual(self.logger_mock.info.call_count, 2)  # One for request, one for response
        
        # Verify request log
        request_call = self.logger_mock.info.call_args_list[0]
        self.assertIn("Request for TestMethod", request_call[0][0])
        self.assertIn("test_value", request_call[0][0])
        
        # Verify response log
        response_call = self.logger_mock.info.call_args_list[1]
        self.assertIn("Response from TestMethod", response_call[0][0])
        self.assertIn("test_value", response_call[0][0])

    def test_message_to_dict_redacts_sensitive_fields(self):
        request_dict = {
            "login": "833671",
            "password": "plain-text-password",
            "nested": {
                "token": "plain-text-token",
                "safe": "visible",
            },
            "items": [
                {
                    "api_key": "plain-text-api-key",
                }
            ],
        }

        with patch.object(mt5_grpc_server.logging_interceptor, "MessageToDict", return_value=request_dict):
            logged_dict = self.interceptor._message_to_dict(SecretMessage())

        self.assertEqual(logged_dict["password"], "[REDACTED]")
        self.assertEqual(logged_dict["nested"]["token"], "[REDACTED]")
        self.assertEqual(logged_dict["items"][0]["api_key"], "[REDACTED]")
        self.assertEqual(logged_dict["login"], "833671")
        self.assertEqual(logged_dict["nested"]["safe"], "visible")

    def test_streaming_response_logging(self):
        # Create mock request and response iterator
        request = TestMessage()
        responses = [TestMessage(), TestMessage()]
        
        # Create mock behavior that returns our response iterator
        behavior = MagicMock(return_value=iter(responses))
        
        # Create mock handler
        handler = self.create_mock_handler(behavior, response_streaming=True)
        
        # Create continuation that returns our handler
        continuation = MagicMock(return_value=handler)
        
        # Get wrapped handler
        wrapped_handler = self.interceptor.intercept_service(continuation, self.handler_details)
        
        # Call the wrapped unary_stream and consume the iterator
        result = list(wrapped_handler.unary_stream(request, self.context))
        
        # Verify the result
        self.assertEqual(len(result), 2)
        self.assertEqual(result, responses)
        
        # Verify logging calls (1 request + 2 responses)
        self.assertEqual(self.logger_mock.info.call_count, 3)
        
        # Verify request log
        request_call = self.logger_mock.info.call_args_list[0]
        self.assertIn("Request for TestMethod", request_call[0][0])
        
        # Verify response logs
        for i in range(2):
            response_call = self.logger_mock.info.call_args_list[i+1]
            self.assertIn("Response from TestMethod", response_call[0][0])

    def test_error_logging(self):
        # Create mock request
        request = TestMessage()
        
        # Create mock behavior that raises an exception
        test_error = Exception("Test error")
        behavior = MagicMock(side_effect=test_error)
        
        # Create mock handler
        handler = self.create_mock_handler(behavior)
        
        # Create continuation that returns our handler
        continuation = MagicMock(return_value=handler)
        
        # Get wrapped handler
        wrapped_handler = self.interceptor.intercept_service(continuation, self.handler_details)
        
        # Call the wrapped unary_unary and expect exception
        with self.assertRaises(Exception) as cm:
            wrapped_handler.unary_unary(request, self.context)
        
        # Verify the exception
        self.assertEqual(str(cm.exception), "Test error")
        
        # Verify error logging
        self.logger_mock.error.assert_called_once()
        error_call = self.logger_mock.error.call_args[0][0]
        self.assertIn("Error in TestMethod", error_call)
        self.assertIn("Test error", error_call)

if __name__ == '__main__':
    unittest.main() 
