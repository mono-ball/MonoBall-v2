"""
Logging configuration for porycon.

Provides centralized logging setup with appropriate levels and formatting.
"""

import logging
import sys
from typing import Optional


def setup_logging(verbose: bool = False, debug: bool = False) -> logging.Logger:
    """
    Configure logging for porycon.
    
    Args:
        verbose: If True, show INFO level messages
        debug: If True, show DEBUG level messages (implies verbose)
    
    Returns:
        Configured logger instance
    """
    # Determine log level
    if debug:
        level = logging.DEBUG
    elif verbose:
        level = logging.INFO
    else:
        level = logging.WARNING
    
    # Configure root logger
    logging.basicConfig(
        level=level,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        handlers=[
            logging.StreamHandler(sys.stdout)
        ],
        force=True  # Override any existing configuration
    )
    
    # Get logger for porycon
    logger = logging.getLogger('porycon')
    logger.setLevel(level)
    
    return logger


def get_logger(name: Optional[str] = None) -> logging.Logger:
    """
    Get a logger instance for a module.
    
    Args:
        name: Optional module name (defaults to 'porycon')
    
    Returns:
        Logger instance
    """
    if name:
        return logging.getLogger(f'porycon.{name}')
    return logging.getLogger('porycon')

