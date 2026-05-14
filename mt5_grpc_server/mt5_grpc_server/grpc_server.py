import argparse
from concurrent import futures
import grpc
import logging
from mt5_grpc_proto import *
from mt5_grpc_proto import (
    account_pb2_grpc,
    common_pb2_grpc,
    deal_pb2_grpc,
    history_orders_pb2_grpc,
    initialize_pb2_grpc,
    market_book_pb2_grpc,
    market_data_pb2_grpc,
    order_calc_pb2_grpc,
    order_check_pb2_grpc,
    order_pb2_grpc,
    position_pb2_grpc,
    symbol_info_pb2_grpc,
    symbol_info_tick_pb2_grpc,
    symbols_pb2_grpc,
    terminal_pb2_grpc,
    trade_pb2_grpc,
)
from .imp import *
from .logging_interceptor import VerboseLoggingInterceptor

def load_credentials(cert_file, private_key_file):
    # Read the certificate and private key files
    with open(cert_file, 'rb') as f:
        certificate_chain = f.read()
    with open(private_key_file, 'rb') as f:
        private_key = f.read()
        
    # Create server credentials
    server_credentials = grpc.ssl_server_credentials(
        [(private_key, certificate_chain)]
    )
    return server_credentials

def setup_logging(verbose):
    logger = logging.getLogger('mt5_grpc_server')
    logger.setLevel(logging.INFO if verbose else logging.WARNING)
    
    # Create console handler with formatting
    handler = logging.StreamHandler()
    formatter = logging.Formatter('%(asctime)s - %(name)s - %(levelname)s - %(message)s')
    handler.setFormatter(formatter)
    logger.addHandler(handler)
    return logger

def main():
    parser = argparse.ArgumentParser(
        description="Start gRPC server"
    )
    parser.add_argument(
        "--host",
        type=str,
        default="0.0.0.0",
        help="Host for gRPC server (default: 0.0.0.0)"
    )
    parser.add_argument(
        "--port",
        type=int,
        default=50051,
        help="Port for gRPC server (default: 50051)"
    )
    parser.add_argument(
        "--secure",
        action="store_true",
        help="Use secure connection with SSL/TLS"
    )
    parser.add_argument(
        "--cert-file",
        type=str,
        help="Path to the SSL certificate file (required if --secure is used)"
    )
    parser.add_argument(
        "--private-key-file",
        type=str,
        help="Path to the private key file (required if --secure is used)"
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable verbose logging of all requests and responses"
    )
    args = parser.parse_args()

    if args.secure and (not args.cert_file or not args.private_key_file):
        parser.error("--cert-file and --private-key-file are required when using --secure")

    # Setup logging
    logger = setup_logging(args.verbose)
    logger.info(f"Starting gRPC server on {args.host}:{args.port} {'(secure)' if args.secure else '(insecure)'}")

    # Create interceptors list
    interceptors = []
    if args.verbose:
        interceptors.append(VerboseLoggingInterceptor())

    # Create server with interceptors
    server = grpc.server(
        futures.ThreadPoolExecutor(max_workers=10),
        interceptors=interceptors
    )

    # Add all services to the server
    account_pb2_grpc.add_AccountInfoServiceServicer_to_server(AccountInfoServiceImpl(), server)
    common_pb2_grpc.add_MetaTraderServiceServicer_to_server(MetaTraderServiceImpl(), server)
    symbol_info_tick_pb2_grpc.add_SymbolInfoTickServiceServicer_to_server(SymbolInfoTickServiceImpl(), server)
    symbol_info_pb2_grpc.add_SymbolInfoServiceServicer_to_server(SymbolInfoServiceImpl(), server)
    trade_pb2_grpc.add_OrderSendServiceServicer_to_server(OrderSendServiceImpl(), server)
    order_calc_pb2_grpc.add_OrderCalcServiceServicer_to_server(OrderCalcServiceImpl(), server)
    order_check_pb2_grpc.add_OrderCheckServiceServicer_to_server(OrderCheckServiceImpl(), server)
    market_data_pb2_grpc.add_MarketDataServiceServicer_to_server(MarketDataServiceImpl(), server)
    market_book_pb2_grpc.add_MarketBookServiceServicer_to_server(MarketBookServiceImpl(), server)
    terminal_pb2_grpc.add_TerminalInfoServiceServicer_to_server(TerminalInfoServiceImpl(), server)
    symbols_pb2_grpc.add_SymbolsServiceServicer_to_server(SymbolsServiceImpl(), server)
    initialize_pb2_grpc.add_InitializeServiceServicer_to_server(InitializeServiceImpl(), server)
    history_orders_pb2_grpc.add_HistoryOrdersServiceServicer_to_server(HistoryOrdersServiceImpl(), server)
    deal_pb2_grpc.add_TradeHistoryServiceServicer_to_server(TradeHistoryServiceImpl(), server)
    order_pb2_grpc.add_OrdersServiceServicer_to_server(OrdersServiceImpl(), server)
    position_pb2_grpc.add_PositionsServiceServicer_to_server(PositionsServiceImpl(), server)

    # Add server port based on security option
    if args.secure:
        server_credentials = load_credentials(args.cert_file, args.private_key_file)
        server.add_secure_port(f'{args.host}:{args.port}', server_credentials)
    else:
        server.add_insecure_port(f'{args.host}:{args.port}')

    server.start()
    server.wait_for_termination()

if __name__ == "__main__":
    main()
